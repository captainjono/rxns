using System;
using System.Collections.Generic;
using System.Reactive.Concurrency;
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
    ///   the stream (e.g. a worker writing typed events to the hub's
    ///   inbound stream).
    ///
    /// - <see cref="Subscribe"/>: read-only side. Poll loop starts immediately
    ///   in the constructor, so the consumer group's reader is alive BEFORE
    ///   any local component can publish - eliminates the "publish before
    ///   anything's listening" race that left every-other event lost on
    ///   concurrent worker boot. Use for the hub's inbound side of the
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

        /// <summary>
        /// The thread subscribers are delivered on, so reading the stream and delivering off it
        /// run at their own speeds.
        ///
        /// <para>Delivery used to happen inline on the poll loop: OnNext ran every subscriber -
        /// the local bus, the tape repo and its disk writes, anything folding reports -
        /// synchronously before the next entry could be read. One slow subscriber therefore set
        /// the drain rate for the whole fleet, and no batch size fixes that. It is why one arena
        /// consumer fell far enough behind that heartbeats published every 5s arrived at 22-25s,
        /// with a 374s tail, and why three rungs finished partial without reporting an error.</para>
        ///
        /// <para>One scheduler, so one thread: events stay ordered and subscribers are never
        /// invoked concurrently, which they were never written to tolerate. What changed is only
        /// that reading no longer waits for delivering.</para>
        /// </summary>
        /// <summary>
        /// Where subscribers observe. The task pool by default, matching every other backing
        /// channel in rxns; settable so a test can pin delivery to a scheduler it controls.
        /// </summary>
        public IScheduler DeliveryScheduler { get; set; } = TaskPoolScheduler.Default;

        /// <summary>
        /// The thread that reads the stream.
        ///
        /// <para>Its own thread, and deliberately not the thread pool. A pool thread doing a
        /// blocking read competes with every other piece of work in the process, and under load
        /// the pool is exactly what is starved - so the drain would slow down precisely when it
        /// most needs not to.</para>
        /// </summary>
        private readonly EventLoopScheduler _pollThread;

        /// <summary>Backlog sampling, off the poll thread so a sample can never delay a read.</summary>
        private readonly IScheduler _sampleScheduler = NewThreadScheduler.Default;

        /// <summary>Everything this channel started, disposed together.</summary>
        private readonly CompositeDisposable _resources = new CompositeDisposable();

        /// <summary>
        /// Events handed to the delivery thread that it has not delivered yet.
        ///
        /// <para>Sampled next to the stream depth because together they say WHERE a backlog is.
        /// Stream deep and this near zero is a slow reader; both deep is a slow subscriber holding
        /// the reader up. Without the second number the two are indistinguishable from outside,
        /// and last time that cost three rungs.</para>
        /// </summary>
        private int _undelivered;

        /// <summary>Names the per-subscriber delivery threads, so a thread dump is readable.</summary>
        private int _subscriberCount;

        /// <summary>
        /// How far delivery may fall behind the read before the drain stops reading.
        ///
        /// <para>This is the backpressure. Without it the poll loop reads and acks at full speed
        /// regardless of whether anything is consuming, so a stalled subscriber converts a Redis
        /// backlog - which is durable, measurable and survives a restart - into an in-memory queue
        /// that is none of those things. Pausing instead leaves the entries in Redis where the
        /// depth and pending counts still report them.</para>
        ///
        /// <para>Generous on purpose: this is a brake for a subscriber in trouble, not a throttle
        /// on a healthy one. A run that sits near this limit has a consumer problem that no buffer
        /// size will fix - watch the sampled undelivered count.</para>
        /// </summary>
        public const int MaxUndelivered = 50000;

        /// <summary>
        /// This channel's own limit. Defaults to <see cref="MaxUndelivered"/>; settable per channel
        /// so the brake can be exercised without publishing fifty thousand messages to watch it.
        /// </summary>
        private readonly int _maxUndelivered;

        private CancellationTokenSource _cts;
        private IDeliveryScheme<T> _deliveryScheme;
        private readonly RedisStreamMode _mode;

        /// <summary>How often the consumer's backlog is sampled, in ms.</summary>
        private const int BacklogSampleMs = 5000;

        /// <summary>
        /// Entries retained per stream, applied at XADD. Null leaves the stream unbounded.
        ///
        /// <para>Acknowledging an entry clears it from the consumer group's pending list and
        /// leaves it on the stream, and nothing here ever calls XTRIM or XDEL - so without a cap
        /// a stream holds every event ever published for the life of the server, in memory, and
        /// whatever RDB persistence is configured writes all of it to disk on every snapshot.</para>
        ///
        /// <para>What the backlog is actually for is a consumer that is not there yet: a group is
        /// created at "0-0", so a host that boots after its publishers replays from the beginning.
        /// That sets the floor - the cap only has to outlast a boot gap or a restart, not a run.</para>
        ///
        /// <para>A process-wide default rather than a constructor argument for the same reason
        /// <c>SignalRRxnManagerBridge.LogBridgeMode</c> is: it is a property of the deployment,
        /// not of one stream, and the host sets it once before any channel is built. Pass
        /// <c>maxStreamLength</c> to override it for a single channel.</para>
        /// </summary>
        public static int? DefaultMaxStreamLength = null;

        private readonly int? _maxStreamLength;

        public RedisStreamBackingChannel(
            string redisConnectionString,
            string streamName,
            string consumerGroup = null,
            bool publishOnly = false,
            RedisStreamMode? mode = null,
            int? maxStreamLength = null,
            int? maxUndelivered = null)
        {
            _streamName = streamName;
            _maxUndelivered = maxUndelivered ?? MaxUndelivered;
            _maxStreamLength = maxStreamLength ?? DefaultMaxStreamLength;
            _consumerGroup = consumerGroup ?? $"{streamName}-group";
            _consumerId = MakeConsumerId(Environment.MachineName);
            // Mode wins when explicitly set; otherwise fall back to the legacy
            // publishOnly bool so existing call-sites keep working unchanged.
            _mode = mode ?? (publishOnly ? RedisStreamMode.PublishOnly : RedisStreamMode.Bidirectional);

            // AbortOnConnectFail=false + LinearRetry(30000) = never stop, 30s
            // between attempts. Worker processes that boot before the hub's
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
            // hub's redis-server. The poll loop retries until it succeeds.
            // Publish-only mode never reads, so it doesn't need the group.

            // Start the poll loop eagerly for any subscribe-capable mode so
            // the consumer-group reader is alive before any Publish in the
            // process can run. Deferring start to Setup() opens a window
            // where pre-Setup publishes land on the stream but are never
            // seen by the local consumer.
            if (_mode != RedisStreamMode.PublishOnly)
            {
                _cts = new CancellationTokenSource();


                _pollThread = new EventLoopScheduler(run => new Thread(run)
                {
                    IsBackground = true,
                    Name = $"redis-poll-{_streamName}"
                });

                // Rx, not Task.Run.
                //
                // These were two fire-and-forget Task.Run calls. Nothing held the returned Task,
                // so an exception escaping either loop faulted it silently: the channel simply
                // stopped draining, for the life of the process, with no log line and no error -
                // and from outside that is indistinguishable from a quiet stream. A consumer that
                // can die without saying so is the worst shape this code can have, because every
                // symptom then points somewhere else.
                //
                // Recursive scheduling on a dedicated thread instead: the scheduler owns the
                // cadence, the subscription is held in _resources so disposal stops it, and an
                // error reaches Until's handler where it is logged and the loop restarted.
                _resources.Add(_pollThread.Schedule(TimeSpan.Zero, PollOnce));
                _resources.Add(Observable
                    .Interval(TimeSpan.FromMilliseconds(BacklogSampleMs), _sampleScheduler)
                    .Subscribe(_ => SampleBacklogOnce()));
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

            // Poll loop already started in the ctor for subscribe-capable modes; Setup just hands
            // back the inbound sequence. Calling Setup twice is safe - it does not restart the poll.
            //
            // Delivery on the task pool, which is what every other channel in rxns does and says
            // why: "We want this channel to be async by default, using the taskpool to schedule
            // observation logic. This will stop any errors from breaking the channel."
            //
            // This channel used to give each subscriber an EventLoopScheduler of its own. That does
            // isolate subscribers from each other - a blocked one owns only its own thread - but it
            // hands a dedicated thread to code this channel does not control, so a subscriber that
            // blocks forever leaks that thread for the life of the process, and a subscriber that
            // throws takes its loop down with it. The pool has neither problem: a blocked callback
            // occupies a pool thread the pool can grow past, and a poison one cannot own a loop
            // there is no loop to own.
            //
            // The per-subscriber counters that bracketed the hand-off are gone with it. They fed a
            // single shared limit, so one subscriber that stopped taking delivery drove that count
            // to the limit alone and paused the poll loop for everybody - measured at 18 of 200
            // messages reaching a subscriber that was doing nothing but accept them.
            return _incoming
                .ObserveOn(DeliveryScheduler)
                .SubscribeOn(DeliveryScheduler);
        }

        public void Publish(T message)
        {
            try
            {
                var json = RxnExtensions.SerialiseImpl(message);
                var typeName = message.GetType().AssemblyQualifiedName;

                // Approximate trimming on purpose: MAXLEN ~ lets redis stop at a whole node
                // boundary, which is amortised O(1). An exact MAXLEN walks the stream on every
                // publish, which is the wrong cost to pay on the hot path to save a few entries.
                _db.StreamAdd(_streamName, new NameValueEntry[]
                {
                    new NameValueEntry("type", typeName),
                    new NameValueEntry("data", json),
                    new NameValueEntry("from", _consumerId)
                }, messageId: null, maxLength: _maxStreamLength, useApproximateMaxLength: true);
            }
            catch (Exception ex)
            {
                $"Failed to publish to Redis stream {_streamName}: {ex.Message}".LogDebug();
                ReconnectIfDead();
            }
        }

        /// <summary>How long to wait before looking again when the stream had nothing.</summary>
        private static readonly TimeSpan IdlePollDelay = TimeSpan.FromMilliseconds(50);

        /// <summary>How long to wait before looking again after an error.</summary>
        private static readonly TimeSpan ErrorPollDelay = TimeSpan.FromSeconds(1);

        /// <summary>Set once the consumer group exists, so the cost is paid once rather than per read.</summary>
        private bool _groupReady;

        /// <summary>
        /// One pass of the drain, then reschedules itself.
        ///
        /// <para>Recursive scheduling rather than a while loop in an async method. The loop's
        /// cadence is the scheduler's business, disposal stops it between passes with nothing to
        /// interrupt mid-read, and - the part that matters - a pass that throws cannot kill the
        /// loop, because the next pass is scheduled in a finally.</para>
        /// </summary>
        private void PollOnce(Action<TimeSpan> again)
        {
            var next = IdlePollDelay;

            try
            {
                if (!EnsureGroup()) { again(ErrorPollDelay); return; }

                // Delivery is behind: stop reading rather than acking into a queue nobody is
                // draining. Re-checked on the next pass, so the drain resumes by itself the moment
                // the subscriber catches up - no signalling, nothing to get wrong.
                if (Volatile.Read(ref _undelivered) >= _maxUndelivered)
                {
                    $"Redis stream drain paused [{_streamName}]: {_undelivered} undelivered, at the {_maxUndelivered} limit - a subscriber is not keeping up".LogDebug();
                    again(IdlePollDelay);
                    return;
                }

                var entries = _db.StreamReadGroup(
                    _streamName,
                    _consumerGroup,
                    _consumerId,
                    count: ReadBatchSize,
                    noAck: false);

                if (entries == null || entries.Length == 0) { again(IdlePollDelay); return; }

                DispatchBatch(entries);

                // Came back full, so there is very likely more waiting: look again immediately
                // rather than sleeping into a backlog we already know about.
                next = entries.Length >= ReadBatchSize ? TimeSpan.Zero : IdlePollDelay;
            }
            catch (Exception ex)
            {
                $"Redis stream poll error [{_streamName}/{_consumerGroup}]: {ex.GetType().Name}: {ex.Message}".LogDebug();
                ReconnectIfDead();
                next = ErrorPollDelay;
            }

            again(next);
        }

        /// <summary>
        /// Creates the consumer group, deferred until redis is actually reachable.
        /// </summary>
        /// <remarks>
        /// Not done in the constructor: AbortOnConnectFail=false means the multiplexer returns
        /// before confirming a connection, so a worker that booted ahead of the hub's redis would
        /// throw on construction and take the whole process with it. From "0-0" so a consumer that
        /// arrives late still replays the backlog rather than silently skipping it.
        /// </remarks>
        private bool EnsureGroup()
        {
            if (_groupReady) return true;

            try
            {
                _db.StreamCreateConsumerGroup(_streamName, _consumerGroup, "0-0", true);
                _groupReady = true;
                $"Redis consumer group ready: {_streamName}/{_consumerGroup}".LogDebug();
            }
            catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP"))
            {
                _groupReady = true;
                $"Redis consumer group ready: {_streamName}/{_consumerGroup}".LogDebug();
            }
            catch (Exception ex)
            {
                $"Redis consumer group create deferred ({_streamName}/{_consumerGroup}): {ex.Message}".LogDebug();
                ReconnectIfDead();
            }

            return _groupReady;
        }

        /// <summary>
        /// Hands one batch to the delivery thread and acks it.
        /// </summary>
        private void DispatchBatch(StreamEntry[] entries)
        {
            // Acks are collected and sent once per batch rather than one round-trip per entry.
            // Per-entry acking made the drain rate a function of Redis RTT: every message cost a
            // serialised round-trip whatever its size, so a busy stream could never be caught up
            // with. Entries that throw are deliberately left out and stay pending, as before.
            var toAck = new List<RedisValue>(entries.Length);

            foreach (var entry in entries)
            {
                string entryTypeName = "<unread>";
                string entryFromId = "<unread>";
                string stage = "init";
                try
                {
                    stage = "read-fields";
                    var typeName = entry["type"].ToString();
                    var json = entry["data"].ToString();
                    var fromId = entry["from"].ToString();
                    entryTypeName = typeName;
                    entryFromId = fromId;

                    // Self-loop suppression: an IRxnBackingChannel is a transport for OTHER nodes'
                    // events. When the publisher and the consumer are the same channel instance,
                    // republishing back onto _incoming creates an infinite cycle:
                    //   Publish -> stream -> Poll -> _incoming -> RxnManager re-routes via central
                    //   -> Publish -> ...
                    // Skip-and-ack anything we sent ourselves; cross-process events arrive with a
                    // different `from` and pass through normally.
                    if (!string.IsNullOrEmpty(fromId) && fromId == _consumerId)
                    {
                        stage = "ack-self";
                        toAck.Add(entry.Id);
                        continue;
                    }

                    stage = "type-resolve";
                    var type = Type.GetType(typeName);
                    if (type == null)
                    {
                        $"Unknown type in stream: {typeName}".LogDebug();
                        toAck.Add(entry.Id);
                        continue;
                    }

                    stage = "deserialise";
                    var obj = RxnExtensions.DeserialiseImpl(type, json);
                    stage = "emit";
                    if (obj is T typed)
                    {
                        // Through the postman, not around it. Setup is handed an IDeliveryScheme
                        // and this channel used to store it and never call it, so whatever delivery
                        // guarantee a caller asked for was silently discarded and everything got
                        // the same hand-rolled behaviour. LocalBackingChannel has always done this
                        // properly - _postman.Deliver(message, e => BackingChannel.OnNext(e)) - and
                        // that is the seam any real guarantee belongs behind.
                        _deliveryScheme.Deliver(typed, e => _incoming.OnNext(e));
                    }
                    else
                    {
                        // Deserialised to null, or to something this channel does not carry. This
                        // used to fall through to the ack below in total silence, so a stream that
                        // was being read perfectly could deliver nothing at all and look healthy
                        // from every angle: group caught up, pending zero, no errors.
                        $"Dropped stream entry {entry.Id}: {typeName} deserialised to {(obj == null ? "null" : obj.GetType().FullName)}, which is not a {typeof(T).Name}".LogDebug();
                    }

                    stage = "ack";
                    toAck.Add(entry.Id);
                }
                catch (Exception ex)
                {
                    // Detail context (type/from/stage) so post-mortems can tell whether the error
                    // is a serialisation issue, a downstream subscriber NRE, or an ack-side blip.
                    $"Error processing stream entry {entry.Id} stage='{stage}' type='{entryTypeName}' from='{entryFromId}': {ex.GetType().Name}: {ex.Message}".LogDebug();
                }
            }

            if (toAck.Count > 0)
                _db.StreamAcknowledge(_streamName, _consumerGroup, toAck.ToArray());
        }

        /// <summary>
        /// Rebuilds the multiplexer when it is dead.
        /// </summary>
        /// <remarks>
        /// StackExchange.Redis's state machine does not recover when the INITIAL connect never
        /// succeeded - it stays "Connecting" forever even after redis comes up. Disposing and
        /// reconstructing against freshly parsed options is what breaks that, and re-parsing
        /// matters: carrying the old options forward carries their cached endpoint-failure state
        /// into the new multiplexer, which then sits in the same limbo.
        /// </remarks>
        private void ReconnectIfDead()
        {
            try
            {
                if (_redis == null || _redis.IsConnected) return;
                $"Redis multiplexer dead (IsConnected=false) - reconnecting [{_streamName}]".LogDebug();
                try { _redis.Dispose(); } catch { }

                var fresh = ConfigurationOptions.Parse(_connectionString);
                fresh.AbortOnConnectFail = false;
                fresh.ReconnectRetryPolicy = new LinearRetry(30000);
                fresh.KeepAlive = 5;
                _redis = ConnectionMultiplexer.Connect(fresh);
                _db = _redis.GetDatabase();

                // The group lives on the server, but this consumer has to re-create it if the
                // server itself was replaced, so make the next pass check again rather than
                // assume.
                _groupReady = false;

                $"Redis reconnect: new multiplexer IsConnected={_redis.IsConnected} [{_streamName}]".LogDebug();
            }
            catch (Exception ex)
            {
                $"Redis reconnect failed [{_streamName}]: {ex.Message}".LogDebug();
            }
        }

        /// <summary>
        /// One backlog sample. Driven by an Rx interval rather than a sleep loop.
        /// </summary>
        private void SampleBacklogOnce()
        {
            try
            {
                if (_db == null) return;

                Emit($"redis.{_streamName}.depth", _db.StreamLength(_streamName));

                // See _undelivered: read next to the depth above, the pair says whether a backlog
                // is on the reading side or the delivering side.
                Emit($"redis.{_streamName}.undelivered", Volatile.Read(ref _undelivered));

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
            catch (Exception ex)
            {
                $"Redis backlog sample failed [{_streamName}]: {ex.Message}".LogDebug();
            }
        }

        /// <summary>
        /// Where backlog samples go. Default is this channel's inbound, which is wrong for every
        /// real owner: the hub's channel belongs to the routed-command hub, which reads Incoming
        /// looking for commands and drops anything else, and a worker's status client is publish-only
        /// so it never reads at all. Emitted samples therefore existed and reached nobody - every
        /// lossless run printed "redis backlog: no samples" and no exported hub carried one.
        /// The owner sets this to put them on a bus something records.
        /// </summary>
        /// <summary>
        /// Entries read per poll.
        ///
        /// <para>Sized so one hub can drain a fleet's telemetry rather than fall behind it. The old
        /// value of 10, combined with an ack round-trip per entry, made the drain rate a function
        /// of Redis latency instead of throughput - every message cost a serialised round-trip
        /// whatever its size, so a busy stream could never be caught up with.</para>
        ///
        /// <para>Raising this alone would not have been enough. Delivery used to run inline on the
        /// poll loop, which made the slowest subscriber the speed limit for the whole stream no
        /// matter how much was read at once; the delivery thread is the half of the fix that
        /// addresses that.</para>
        /// </summary>
        public const int ReadBatchSize = 200;

        public Action<T> MetricSink { get; set; }

        /// <summary>
        /// Publishes one sample as a queue snapshot, which is literally what a stream depth is.
        ///
        /// <para>Not as a TimeSeriesData, though that is what ends up in the metrics tape. Nothing
        /// collects loose TimeSeriesData off the bus: SystemMetricsAggregateView <i>produces</i> it by
        /// reacting to health events like this one. Publishing the finished article skipped the only
        /// thing that would have recorded it, which is why the samples existed for weeks, reached the
        /// bus, and still never appeared in a single export.</para>
        /// </summary>
        private void Emit(string name, double value)
        {
            if (!(new Rxns.Health.QueueSnapshotEvent(name, TimeSpan.Zero, (long)value) is T sample)) return;

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
            // consumers, never ACK, and never reach the live hub.
            // Symptom: hub seeing only every-other worker's heartbeats
            // after a few test-class iterations.
            try
            {
                if (_mode != RedisStreamMode.PublishOnly && _db != null)
                    _db.StreamDeleteConsumer(_streamName, _consumerGroup, _consumerId);
            }
            catch { /* best-effort cleanup */ }

            // Nothing to stop on the delivery side any more: subscribers observe on the task pool,
            // which this channel does not own and must not tear down. Anything still queued there
            // is dropped when the subject completes, which is correct at shutdown - the alternative
            // is a dispose that waits on whatever slow subscriber caused the queue in the first
            // place.

            _incoming?.Dispose();
            _redis?.Dispose();
        }
    }
}
