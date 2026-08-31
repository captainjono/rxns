using System;
using System.Reactive.Disposables;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Rxns;
using Rxns.Interfaces;
using Rxns.Logging;
using StackExchange.Redis;

namespace Rxns.Redis
{
    /// <summary>
    /// Redis Streams backing channel for distributed event processing.
    /// Each reactor gets its own stream — the stream name is host-supplied.
    ///
    /// Why Redis Streams (not Pub/Sub):
    /// - Persistent: messages survive consumer restarts
    /// - Consumer groups: multiple pods share work (no duplicates)
    /// - KEDA scalable: stream length is a native metric
    /// - Acknowledgement: messages only removed after processing
    /// </summary>
    /// <summary>
    /// Wire-mode for <see cref="RedisStreamBackingChannel{T}"/>:
    ///
    /// - <see cref="PublishOnly"/>: write-only side of the wire. No poll loop
    ///   is started, Setup returns Empty. Use for nodes that only emit onto
    ///   the stream (e.g. a worker writing typed events to the arena's
    ///   inbound stream).
    ///
    /// - <see cref="Subscribe"/>: read-only side. Poll loop starts immediately
    ///   in the constructor, so the consumer group's reader is alive BEFORE
    ///   any local component can publish - eliminates the "publish before
    ///   anything's listening" race that left every-other event lost on
    ///   concurrent worker boot. Use for the arena's inbound side of the
    ///   typed-events stream / a worker's inbound side of the routed-cmds
    ///   stream.
    ///
    /// - <see cref="Bidirectional"/>: both publish AND subscribe. Same
    ///   eager-start semantics as Subscribe; the publish path is just
    ///   additionally permitted. Useful for the few places that need both
    ///   on the same channel.
    /// </summary>
    public enum RedisStreamMode
    {
        Bidirectional = 0,
        PublishOnly = 1,
        Subscribe = 2
    }

    public class RedisStreamBackingChannel<T> : IRxnBackingChannel<T>, IDisposable where T : class
    {
        private readonly string _streamName;
        private readonly string _consumerGroup;
        private readonly string _consumerId;
        private readonly string _connectionString;
        private readonly ConfigurationOptions _opts;
        private ConnectionMultiplexer _redis;
        private IDatabase _db;
        private readonly Subject<T> _incoming = new Subject<T>();
        private CancellationTokenSource _cts;
        private IDeliveryScheme<T> _deliveryScheme;
        private readonly RedisStreamMode _mode;

        /// <summary>How often the consumer's backlog is sampled, in ms.</summary>
        private const int BacklogSampleMs = 5000;

        public RedisStreamBackingChannel(
            string redisConnectionString,
            string streamName,
            string consumerGroup = null,
            bool publishOnly = false,
            RedisStreamMode? mode = null)
        {
            _streamName = streamName;
            _consumerGroup = consumerGroup ?? $"{streamName}-group";
            _consumerId = MakeConsumerId(Environment.MachineName);
            // Mode wins when explicitly set; otherwise fall back to the legacy
            // publishOnly bool so existing call-sites keep working unchanged.
            _mode = mode ?? (publishOnly ? RedisStreamMode.PublishOnly : RedisStreamMode.Bidirectional);

            // AbortOnConnectFail=false + LinearRetry(30000) = never stop, 30s
            // between attempts. Worker processes that boot before the arena's
            // redis-server is bound stay alive and silently retry forever
            // until redis comes up — same behaviour we want for transient
            // network hiccups in steady-state. With the default (abort=true,
            // policy=ExponentialRetry capped) workers throw on construction
            // and the cluster stays degraded until manual restart.
            // Caller-supplied options string still wins for any explicitly
            // set field; we only fill in unset / weaker defaults.
            _connectionString = redisConnectionString;
            _opts = ConfigurationOptions.Parse(redisConnectionString);
            _opts.AbortOnConnectFail = false;
            _opts.ReconnectRetryPolicy = new LinearRetry(30000);
            // KeepAlive triggers the background reconnect probe. Default 60s
            // means a multiplexer that fails to connect at construct stays
            // dead for a full minute before attempting to reconnect even after
            // redis comes up. 5s gives near-immediate recovery once the boot
            // race resolves.
            if (_opts.KeepAlive <= 0 || _opts.KeepAlive > 5) _opts.KeepAlive = 5;
            _redis = ConnectionMultiplexer.Connect(_opts);
            _db = _redis.GetDatabase();

            // Consumer group creation is deferred to the poll loop. With
            // AbortOnConnectFail=false the multiplexer returns immediately
            // even when redis is unreachable; calling StreamCreateConsumerGroup
            // here would throw on a fresh worker that booted before the
            // arena's redis-server. The poll loop retries until it succeeds.
            // Publish-only mode never reads, so it doesn't need the group.

            // Start the poll loop eagerly for any subscribe-capable mode so
            // the consumer-group reader is alive before any Publish in the
            // process can run. Deferring start to Setup() opens a window
            // where pre-Setup publishes land on the stream but are never
            // seen by the local consumer.
            if (_mode != RedisStreamMode.PublishOnly)
            {
                _cts = new CancellationTokenSource();
                Task.Run(() => PollStream(_cts.Token));
                Task.Run(() => SampleBacklog(_cts.Token));
            }
        }

        /// <summary>
        /// Per-process consumer id. MUST be unique: the poll loop treats an entry whose
        /// <c>from</c> equals this id as self-published and skip-and-acks it silently.
        /// <para>Collisions are silent, so truncate the host part if you must - never the entropy.
        /// Hosts sharing a prefix are the common case (a VMSS names its instances
        /// <c>&lt;cluster&gt;00000N</c>), and a collision leaves every node discarding the others'
        /// events as its own echo.</para>
        /// </summary>
        public static string MakeConsumerId(string machineName)
        {
            var host = string.IsNullOrWhiteSpace(machineName) ? "host" : machineName;
            if (host.Length > 12) host = host.Substring(0, 12);
            return $"{host}-{Guid.NewGuid():N}";
        }

        public IObservable<T> Setup(IDeliveryScheme<T> postman)
        {
            _deliveryScheme = postman;

            if (_mode == RedisStreamMode.PublishOnly)
                return Observable.Empty<T>();

            // Poll loop already started in the ctor for subscribe-capable
            // modes; Setup just hands back the inbound observable. Calling
            // Setup multiple times is safe - we don't restart the poll.
            return _incoming.AsObservable();
        }

        public void Publish(T message)
        {
            try
            {
                var json = RxnExtensions.SerialiseImpl(message);
                var typeName = message.GetType().AssemblyQualifiedName;

                _db.StreamAdd(_streamName, new NameValueEntry[]
                {
                    new NameValueEntry("type", typeName),
                    new NameValueEntry("data", json),
                    new NameValueEntry("from", _consumerId)
                });
            }
            catch (Exception ex)
            {
                $"Failed to publish to Redis stream {_streamName}: {ex.Message}".LogDebug();
                ReconnectIfDead();
            }
        }

        private async Task PollStream(CancellationToken ct)
        {
            $"Redis stream consumer started: {_streamName}/{_consumerGroup}/{_consumerId}".LogDebug();

            // Lazy consumer-group creation. Retries every 5s until redis is
            // reachable AND the group is set up. AbortOnConnectFail=false
            // means the ctor returned without confirming a connection, so the
            // worker survives a redis-not-yet-up boot race; this loop bridges
            // the gap.
            //
            // ReconnectIfDead: StackExchange.Redis's multiplexer state machine
            // doesn't recover when the INITIAL connect never succeeded — it
            // stays "Connecting" forever even after redis comes up. To break
            // that, when an operation fails AND IsConnected is false, dispose
            // the dead multiplexer and reconstruct against the same
            // ConfigurationOptions. New multiplexer probes the now-up redis
            // and connects cleanly.
            var groupReady = false;
            while (!groupReady && !ct.IsCancellationRequested)
            {
                try
                {
                    _db.StreamCreateConsumerGroup(_streamName, _consumerGroup, "0-0", true);
                    groupReady = true;
                }
                catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP"))
                {
                    groupReady = true;
                }
                catch (Exception ex)
                {
                    $"Redis consumer group create deferred ({_streamName}/{_consumerGroup}): {ex.Message}".LogDebug();
                    ReconnectIfDead();
                    await Task.Delay(5000, ct);
                }
            }
            $"Redis consumer group ready: {_streamName}/{_consumerGroup}".LogDebug();

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var entries = await _db.StreamReadGroupAsync(
                        _streamName,
                        _consumerGroup,
                        _consumerId,
                        count: 10,
                        noAck: false);

                    if (entries == null || entries.Length == 0)
                    {
                        await Task.Delay(50, ct);
                        continue;
                    }

                    foreach (var entry in entries)
                    {
                        string entryTypeName = "<unread>";
                        string entryFromId   = "<unread>";
                        string stage         = "init";
                        try
                        {
                            stage = "read-fields";
                            var typeName = entry["type"].ToString();
                            var json = entry["data"].ToString();
                            var fromId = entry["from"].ToString();
                            entryTypeName = typeName;
                            entryFromId   = fromId;

                            // Self-loop suppression: an IRxnBackingChannel is a transport
                            // for OTHER nodes' events. When the publisher and the consumer
                            // are the same channel instance, republishing the message back
                            // onto _incoming creates an infinite cycle:
                            //   Publish → stream → Poll → _incoming → RxnManager re-routes
                            //   via central → Publish → ...
                            // Skip-and-ack anything we sent ourselves; cross-process events
                            // arrive with a different `from` and pass through normally.
                            if (!string.IsNullOrEmpty(fromId) && fromId == _consumerId)
                            {
                                stage = "ack-self";
                                await _db.StreamAcknowledgeAsync(_streamName, _consumerGroup, entry.Id);
                                continue;
                            }

                            stage = "type-resolve";
                            var type = Type.GetType(typeName);
                            if (type == null)
                            {
                                $"Unknown type in stream: {typeName}".LogDebug();
                                await _db.StreamAcknowledgeAsync(_streamName, _consumerGroup, entry.Id);
                                continue;
                            }

                            stage = "deserialise";
                            var obj = RxnExtensions.DeserialiseImpl(type, json);
                            stage = "emit";
                            if (obj is T typed)
                            {
                                _incoming.OnNext(typed);
                            }

                            stage = "ack";
                            await _db.StreamAcknowledgeAsync(_streamName, _consumerGroup, entry.Id);
                        }
                        catch (Exception ex)
                        {
                            // Detail context (type/from/stage) so post-mortems can tell
                            // whether the error is a serialisation issue, a downstream
                            // subscriber NRE, or an ack-side network blip.
                            $"Error processing stream entry {entry.Id} stage='{stage}' type='{entryTypeName}' from='{entryFromId}': {ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}".LogDebug();
                        }
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    $"Redis stream poll error [{_streamName}/{_consumerGroup}]: {ex.GetType().Name}: {ex.Message}".LogDebug();
                    ReconnectIfDead();
                    await Task.Delay(1000, ct);
                }
            }
        }

        private void ReconnectIfDead()
        {
            try
            {
                if (_redis == null || _redis.IsConnected) return;
                $"Redis multiplexer dead (IsConnected=false) — reconnecting [{_streamName}]".LogDebug();
                try { _redis.Dispose(); } catch { }
                // Re-parse options so we don't carry forward any cached
                // endpoint-failure state that could keep the new multiplexer
                // in the same Connecting/NotStarted limbo.
                var fresh = ConfigurationOptions.Parse(_connectionString);
                fresh.AbortOnConnectFail   = false;
                fresh.ReconnectRetryPolicy = new LinearRetry(30000);
                fresh.KeepAlive            = 5;
                _redis = ConnectionMultiplexer.Connect(fresh);
                _db    = _redis.GetDatabase();
                $"Redis reconnect: new multiplexer IsConnected={_redis.IsConnected} [{_streamName}]".LogDebug();
            }
            catch (Exception ex)
            {
                $"Redis reconnect failed [{_streamName}]: {ex.Message}".LogDebug();
            }
        }

        /// <summary>
        /// Publishes how far behind the consumer is, every few seconds, as plain time-series metrics.
        ///
        /// <para>Backpressure is invisible without this. When the arena cannot keep up, work does not
        /// fail - it queues, which is the whole reason to prefer a stream over synchronous HTTP - but
        /// a queue nobody measures looks the same as a healthy system right up until it does not. The
        /// stream depth and the group's unacknowledged count are the two numbers that say whether the
        /// cluster is keeping pace, and they belong on the same timeline as the client latency the
        /// perf report already plots.</para>
        ///
        /// <para>Emitted onto the inbound stream rather than published back to Redis: these describe
        /// the transport, so routing them through the transport they describe would add to the load
        /// being measured and risk the self-loop the poll path guards against.</para>
        /// </summary>
        private async Task SampleBacklog(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(BacklogSampleMs, ct).ConfigureAwait(false);
                    if (_db == null) continue;

                    Emit($"redis.{_streamName}.depth", _db.StreamLength(_streamName));

                    try
                    {
                        Emit($"redis.{_streamName}.pending", _db.StreamPending(_streamName, _consumerGroup).PendingMessageCount);
                    }
                    catch
                    {
                        // No consumer group yet, or a server without XPENDING. Depth alone still says
                        // whether the stream is growing, so do not lose the sample over it.
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    $"Redis backlog sample failed [{_streamName}]: {ex.Message}".LogDebug();
                }
            }
        }

        /// <summary>
        /// Where backlog samples go. Default is this channel's inbound, which is wrong for every
        /// real owner: the arena's channel belongs to the routed-command hub, which reads Incoming
        /// looking for commands and drops anything else, and a worker's status client is publish-only
        /// so it never reads at all. Emitted samples therefore existed and reached nobody - every
        /// lossless run printed "redis backlog: no samples" and no exported arena carried one.
        /// The owner sets this to put them on a bus something records.
        /// </summary>
        public Action<T> MetricSink { get; set; }

        private void Emit(string name, double value)
        {
            if (!(new Rxns.Metrics.TimeSeriesData
            {
                Name = name,
                TimeStamp = DateTime.UtcNow,
                Value = value
            } is T sample)) return;

            var sink = MetricSink;
            try { if (sink != null) sink(sample); else _incoming.OnNext(sample); }
            catch { /* shutting down */ }
        }

        public void Dispose()
        {
            _cts?.Cancel();
            // Deregister this consumer from the group on graceful shutdown.
            // Without this, every restart leaves a ghost consumer in the
            // group and Redis load-balances new messages across all of them
            // (alive AND dead). Half the messages get delivered to dead
            // consumers, never ACK, and never reach the live arena.
            // Symptom: arena seeing only every-other worker's heartbeats
            // after a few test-class iterations.
            try
            {
                if (_mode != RedisStreamMode.PublishOnly && _db != null)
                    _db.StreamDeleteConsumer(_streamName, _consumerGroup, _consumerId);
            }
            catch { /* best-effort cleanup */ }
            _incoming?.Dispose();
            _redis?.Dispose();
        }
    }
}
