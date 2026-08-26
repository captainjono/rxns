using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.IO;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive.Threading.Tasks;
using System.Threading;
using System.Threading.Tasks;
using Autofac.Features.OwnedInstances;
using Microsoft.AspNet.SignalR.Client;
using Microsoft.AspNetCore.SignalR.Client;
using Newtonsoft.Json;
using Rxns.Cloud;
using Rxns.Health;
using Rxns.Hosting;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns.WebApiNET5.NET5WebApiAdapters
{
    public class SignalRRxnManagerBridge : HttpAppStatusServiceClient, IEventHubClient, IAppStatusServiceClient, IRxnPublisher<IRxn> //ibackingchannel
    {
        public IScheduler DefaultScheduler { get; set; }

        public string Url { get; set; }

        private readonly BehaviorSubject<bool> _isConnected = new BehaviorSubject<bool>(false);
        public IObservable<bool> IsConnected
        {
            get { return _isConnected; }
        }

        private Subject<IRxn> _remoteEvents;
        private readonly Func<string, Owned<HubConnection>> _hubClientFactory;
        private readonly IRouteProvider _systemInfo;
        private Owned<HubConnection> _connection;

        private readonly Subject<IRxn> publishChannel = new Subject<IRxn>();

        private readonly List<IDisposable> _connectionResources = new List<IDisposable>();
        private readonly List<IDisposable> _isConnectedResources = new List<IDisposable>();
        private readonly IAuthenticationService<AccessToken, ITenantCredentials> _authenticationService;
        //private IHubProxy _clientProxy;
        private Action<IRxn> _publish;
        private IResolveTypes _resolver;

        // Buffer for events published before ConfigiurePublishFunc wires the real publisher.
        // Bounded so a broken DI hookup can't grow unbounded - at capacity we drop oldest +
        // warn. Flushed in declaration order when the real publisher is installed.
        private const int PreConfigBufferCap = 10_000;
        private readonly ConcurrentQueue<IRxn> _preConfigBuffer = new ConcurrentQueue<IRxn>();
        private int _droppedBeforeConfig;

        // Buffer for outbound publish batches issued while the SignalR transport
        // is between Connected states (Reconnecting / Closed / Connecting). Each
        // entry is a pre-serialised IRxn[] ready for InvokeAsync("PublishBatch", …).
        // Drained on every Reconnected event AND on every fresh connect() success.
        // Bounded — at capacity we drop oldest + tick a counter so an offline
        // worker can't grow unbounded waiting for the arena to come back.
        private const int PendingPublishesCap = 500;
        private readonly ConcurrentQueue<object[]> _pendingPublishes = new ConcurrentQueue<object[]>();
        private int _droppedPending;

        // Per-batch byte-size cap fed to BufferUntilSize. Server-side MapHub
        // ApplicationMaxBufferSize / TransportMaxBufferSize / HubOptions.MaximumReceiveMessageSize
        // are 4 MB; 3 MB leaves a 1 MB safety margin for SignalR framing +
        // type-discriminator overhead. Lets a single batch carry many IRxns
        // — fewer round-trips, fewer in-flight RPC calls on reconnect drain.
        // If a future event blows past 3 MB on its own, bump both the server
        // cap AND this together, or truncate at source.
        private const int BatchSizeBytesCap = 3 * 1024 * 1024;

        // How many in-flight PublishBatch invocations DrainPendingPublishes will
        // hold open at once. Without this cap, draining the full pending queue
        // on reconnect fires every batch into client.InvokeAsync simultaneously
        // and overloads the SignalR server, triggering another disconnect and
        // re-storming. Throttling to a small steady concurrency lets the
        // server keep up.
        private const int DrainConcurrencyCap = 4;

        // Rough size estimator — Newtonsoft-serialise the IRxn and count
        // chars. Cheap (no reflection beyond what serialise already does)
        // and accurate enough for batch-cap purposes; serialising twice
        // (once here, once in the actual publish) is wasteful but the
        // alternative — caching the serialised form alongside the IRxn —
        // requires a wrapper type that ripples through the publish path.
        private static int EstimateIRxnSize(IRxn msg)
        {
            try { return msg.Serialise()?.Length ?? 0; } catch { return 0; }
        }


        public SignalRRxnManagerBridge(Func<string, Owned<HubConnection>> hubClientFactory, IRouteProvider systemInfo, IRxnAppInfo appInfo, IAuthenticationService<AccessToken, ITenantCredentials> authenticationService, IHttpConnection client, ICreateEvents eventFactory, ITenantCredentials credentials, IAppServiceRegistry apps, IResolveTypes resolver, IScheduler scheduler = null) : base(client, eventFactory, appInfo, credentials, apps)
        {
            // Queue instead of dropping - ConfigiurePublishFunc replays the buffer once DI
            // resolution installs the real publisher. Previously every IRxn emitted during
            // bridge construction (e.g. UnitTestResult on fast test suites) was silently lost.
            _publish = msg =>
            {
                if (_preConfigBuffer.Count >= PreConfigBufferCap)
                {
                    _preConfigBuffer.TryDequeue(out _);
                    Interlocked.Increment(ref _droppedBeforeConfig);
                }
                _preConfigBuffer.Enqueue(msg);
            };
            DefaultScheduler = scheduler ?? TaskPoolScheduler.Default;
            _hubClientFactory = hubClientFactory;
            _systemInfo = systemInfo;
            _authenticationService = authenticationService;
            _resolver = resolver;

            Url = WithBaseUrl("EventsHub");
            Connect().Until(OnError);
        }

        // Startup race: a client that boots before its server is listening is refused, and the
        // retry can land while the transport is still Connecting. Deferring must re-arm - a
        // deferral that drops the chain leaves the client silently on the fallback channel.
        public const int ConnectRetrySeconds = 2;

        /// <summary>
        /// True while the transport is mid-transition, so an attempt now would be a no-op.
        /// </summary>
        public static bool ShouldDeferConnect(HubConnectionState state)
        {
            return state != HubConnectionState.Disconnected;
        }

        /// <summary>
        /// Re-arms a deferred attempt: wait, look again, fire once the transport is Disconnected.
        /// Stops on Connected, so a healthy fleet pays nothing for it.
        /// </summary>
        public static IDisposable DeferConnect(IScheduler scheduler, Func<HubConnectionState> currentState, Action attempt, TimeSpan delay)
        {
            return Observable.Interval(delay, scheduler)
                .TakeWhile(_ => currentState() != HubConnectionState.Connected)
                .Where(_ => !ShouldDeferConnect(currentState()))
                .Take(1)
                .Do(_ => attempt())
                // Until, not Subscribe: a throw out of attempt() is reported rather than becoming
                // an unobserved exception.
                .Until(e => $"SignalR deferred connect attempt failed: {e}".LogDebug());
        }

        /// <summary>
        /// Connects to the SignalR hub specified in the Url
        /// </summary>
        public IObservable<Unit> Connect()
        {
            return Observable.Create<Unit>(o =>
            {
                try
                {
                    if (_connection == null)
                    {
                        OnInformation("Connecting to: '{0}'", Url);

                        //get new client from factory
                        _connection = _hubClientFactory(Url); //singleton now //.DisposedBy(_connectionResources);
                        //Add logging to the client
                        var client = _connection.Value; //.ReportsWith(this, _connectionResources);

                        Func<IDisposable> connect = null;
                        connect = () =>
                        {

                            //already connecting? defer and look again
                            if (ShouldDeferConnect(client.State))
                                return DeferConnect(DefaultScheduler, () => client.State, () => connect(), TimeSpan.FromSeconds(ConnectRetrySeconds));

                            lock (_isConnectedResources)
                            {
                                _isConnectedResources.DisposeAll();
                                _isConnectedResources.Clear();
                            }

                            return TimeSpan.FromSeconds(1).Then().SelectMany(_ =>
                                    
                            client.StartAsync()
                                .ToObservable()
                                .Do(t =>
                                {
                                    lock (_isConnectedResources)
                                    {
                                        client.InvokeAsync("RegisterAsService", _systemInfo.GetLocalBaseRoute());
                                        //setup the publish channel — queues batches
                                        //when transport isn't Connected, replays
                                        //on the next Connected window. Survives
                                        //arena bounces / SignalR auto-reconnect.
                                        // Size-based batching: a batch is emitted when
                                        // EITHER cumulative serialised bytes would exceed
                                        // BatchSizeBytesCap OR 200ms elapses. Independent
                                        // of item count — so a 5000-line cat produces as
                                        // many small batches as needed (each safely under
                                        // SignalR's MaximumReceiveMessageSize) instead of
                                        // one giant batch that gets rejected. See
                                        // Rxns.BufferUntilSize for the operator. Single
                                        // IRxn larger than cap is the producer's problem
                                        // (truncate at source); batcher emits it alone.
                                        publishChannel
                                            .BufferUntilSize(EstimateIRxnSize, BatchSizeBytesCap, TimeSpan.FromMilliseconds(200))
                                            .Where(batch => batch.Count > 0)
                                            .Do(batch =>
                                            {
                                                var serialized = batch.Select(msg => msg.Serialise().ResolveAs(msg.GetType())).ToArray();
                                                PublishOrQueue(client, serialized);
                                            })
                                            .Until(err => $"SignalRBridge publish pipe error: {err.Message}".LogDebug())
                                            .DisposedBy(_isConnectedResources);

                                        // Drain anything that built up while we
                                        // were Reconnecting / Closed.
                                        DrainPendingPublishes(client);

                                        _isConnected.OnNext(true);


                                        client.On<IRxn>("RemoteCommand", action => { _publish(action); })
                                            .DisposedBy(_isConnectedResources);

                                        //should be called in createsubscript, not here
                                        client.On<string>("Subscribe",
                                            action =>
                                            {
                                                _publish((IRxn) action.Deserialise(action.GetTypeFromJson(_resolver)));
                                            }).DisposedBy(_isConnectedResources);
                                    }

                                })
                            )
                            .Until(e =>
                            {
                                o.OnError(e);
                                connect();
                            });
                        };


                        client.Reconnecting += exception =>
                        {
                            OnError("Reconnecting!", exception);
                            _isConnected.OnNext(false);

                            return Task.CompletedTask;
                        };

                        client.Reconnected += async s =>
                        {
                            // SignalR auto-reconnect issues a NEW connectionId.
                            // Server-side EventsHub._routes[route] was cleared
                            // on the original disconnect, so without this call
                            // every IRxnQuestion dispatched to this worker's
                            // route after a reconnect queues forever (operator
                            // sees Remote Shell hang silently). The server's
                            // RegisterAsService is idempotent (Add-or-update
                            // _routes[route] = connectionId), so calling it
                            // unconditionally on every reconnect is safe.
                            try
                            {
                                await client.InvokeAsync("RegisterAsService", _systemInfo.GetLocalBaseRoute());
                                $"SignalRBridge reconnected as '{s}'; re-registered route '{_systemInfo.GetLocalBaseRoute()}'".LogDebug();
                            }
                            catch (Exception ex)
                            {
                                $"SignalRBridge reconnect re-RegisterAsService failed: {ex.Message}".LogDebug();
                            }
                            // Replay anything that piled up while the transport
                            // was Reconnecting — events that hit publishChannel
                            // mid-bounce queued via EnqueuePending instead of
                            // hitting InvokeAsync against a non-Connected state.
                            DrainPendingPublishes(client);
                            _isConnected.OnNext(true);
                        };

                        client.Closed += exception =>
                        {
                            "Connection closed!".LogDebug(Url);
                            _isConnected.OnNext(false);

                            connect();

                            return Task.CompletedTask;
                        };

                        //setup proxy then start
                        OnVerbose("Connecting bridge");

                        //setup authentication
                        return client
                            .WithAuthentication(_authenticationService)
                            .Select(_ => connect())
                            .Until(error =>
                        {
                            _isConnected.OnNext(false);

                            o.OnError(error);
                        })
                        .DisposedBy(_connectionResources);
                    }
                }
                catch (Exception e)
                {
                    OnError(e);
                }

                return Disposable.Empty;
            });
        }

        //private void OnConnectionStateChanged(ConnectionState state, IObserver<Unit> connectionStateStream)
        //{
        //    switch (state.NewState)
        //    {

        //        //disconnection is only an error when a client is already connecting/connected 
        //        case ConnectionState.Reconnecting:
        //            OnVerbose("Reconnecting");
        //            break;
        //        case ConnectionState.Disconnected:
        //            OnVerbose("Disconnecting");
        //            _isConnected.OnNext(false);
        //            _isConnectedResources.DisposeAll();
        //            _isConnectedResources.Clear();
        //            break;
        //        case ConnectionState.Connected:
        //            OnVerbose("Connected");
        //            //_route =
        //                //RemoteCommandEvent.ForTenant<RemoteCommandEvent>(_configuration.Tenant, _systemInfo.Name, ReporterName).Destination;
                  



        //            if(false) //need to make configurable. this can flood otherwise
        //                _clientProxy.On<IRxn>("LogReceived", action =>
        //                {
        //                    _publish(action);
        //                }).DisposedBy(_isConnectedResources);


        //            _isConnected.OnNext(true);
        //            connectionStateStream.OnNext(new Unit());
        //            connectionStateStream.OnCompleted();
        //            break;
        //    }
        //}

        /// <summary>
        /// Disconnects from the SignalR hub
        /// </summary>
        public void Disconnect()
        {
            if (_connection != null)
            {
                OnInformation("Disconnecting client");

                this.ReportExceptions(() =>
                {
                    _connection.Value.InvokeAsync("RemoveRegistration", _systemInfo.GetLocalBaseRoute());
                });

                _isConnected.OnNext(false);
                _isConnectedResources.DisposeAll();
                _isConnectedResources.Clear();

                //dispose all resources
                _connectionResources.DisposeAll();
                _connectionResources.Clear();
                _connection = null;

            }
        }

        /// <summary>
        /// Log-bridge mode — controls which log-class IRxns (RLM) cross the
        /// SignalR bridge. Settable at process startup by the host. Affects
        /// realtime UI streaming only — the file log + end-of-run zip ride
        /// independently. Values: "verbose" (default, all RLMs cross),
        /// "errors" (only Error/Warning cross), "off" (no RLMs cross).
        /// </summary>
        public static string LogBridgeMode = "verbose";

        private static bool ShouldDropRlm(IRxn message)
        {
            // Fast path: not a log event, never drop.
            if (message is not Rxns.Logging.RLM rlm) return false;
            var mode = LogBridgeMode;
            if (mode == "verbose" || string.IsNullOrEmpty(mode)) return false;
            if (mode == "off") return true;
            if (mode == "errors")
            {
                // RLM.L embeds the formatted level marker like "[Error]" / "[Warning]"
                // (per LogMessage.ToString in Rxns.Logging). Drop anything not at those
                // two levels — substring match keeps this allocation-free.
                var l = rlm.L;
                if (string.IsNullOrEmpty(l)) return true;
                return l.IndexOf("[Error]", StringComparison.Ordinal) < 0
                    && l.IndexOf("[Warning]", StringComparison.Ordinal) < 0
                    && l.IndexOf("[Fatal]", StringComparison.Ordinal) < 0;
            }
            return false;
        }

        public void Publish(IRxn message)
        {
            this.ReportExceptions(() =>
            {
                if (ShouldDropRlm(message)) return;
                publishChannel.OnNext(message);
            });
        }


        public IObservable<IRxn> CreateSubscription()
        {
            OnInformation("Creating subscription for remote events --- NEED TO FIX - WONT WORK! - need to deserilise properly here");

            if (_remoteEvents == null)
            {
                _remoteEvents = new Subject<IRxn>();

                _connection.Value.On<string>("Subscribe",
                    (message) =>
                        this.ReportExceptions(() =>
                        {
                            var msg = message.Deserialise(message.GetTypeFromJson(_resolver));
                            _remoteEvents.OnNext((IRxn)msg);
                        }))
                    .DisposedBy(_connectionResources);
            }

            return _remoteEvents;
        }

        public override IObservable<Unit> Publish(IEnumerable<IRxn> events)
        {
            return Rxn.Create(() =>
            {
                events.ForEach(e => Publish(e));
            });
        }

        public override IObservable<Unit> PublishError(BasicErrorReport report)
        {
            return Rxn.Create(() => Publish(report));
        }

        public override IObservable<IRxnQuestion[]> PublishSystemStatus(SystemStatusEvent status, AppStatusInfo[] meta)
        {
            return Rxn.Create<IRxnQuestion[]>(() => Publish(new AppHeartbeat(status, meta)));
        }

        public void ConfigiurePublishFunc(Action<IRxn> publish)
        {
            _publish = publish;
            if (_droppedBeforeConfig > 0)
                $"SignalRRxnManagerBridge: dropped {_droppedBeforeConfig} pre-config IRxns (buffer cap {PreConfigBufferCap})".LogDebug();
            while (_preConfigBuffer.TryDequeue(out var queued))
                publish(queued);
        }

        /// <summary>
        /// Enqueue a pre-serialised publish batch for replay on next Connected
        /// transition. Bounded — drops oldest when full so an offline worker
        /// can't grow the queue unbounded.
        /// </summary>
        private void EnqueuePending(object[] serializedBatch)
        {
            if (_pendingPublishes.Count >= PendingPublishesCap)
            {
                _pendingPublishes.TryDequeue(out _);
                Interlocked.Increment(ref _droppedPending);
            }
            _pendingPublishes.Enqueue(serializedBatch);
        }

        /// <summary>
        /// Publish a batch via SignalR if the transport is up; otherwise queue
        /// for replay. Even on the up-path we attach a fault continuation so
        /// an InvokeAsync that fails async (server restart mid-call, network
        /// blip) requeues the batch instead of losing it. The next Connected
        /// transition's <see cref="DrainPendingPublishes"/> will replay it.
        /// </summary>
        private void PublishOrQueue(HubConnection client, object[] serializedBatch)
        {
            if (client.State != HubConnectionState.Connected)
            {
                EnqueuePending(serializedBatch);
                return;
            }
            try
            {
                var task = client.InvokeAsync("PublishBatch", (object)serializedBatch);
                task.ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        var inner = t.Exception?.GetBaseException();
                        $"PublishBatch failed: {inner?.GetType().Name}: {inner?.Message} - re-queueing for next Connected window".LogDebug();
                        EnqueuePending(serializedBatch);
                    }
                }, TaskContinuationOptions.ExecuteSynchronously);
            }
            catch (Exception ex)
            {
                // Synchronous throw (e.g. ObjectDisposedException after a
                // racing Closed). Still recoverable — queue for the new
                // connection's drain.
                $"PublishBatch sync-threw: {ex.GetType().Name}: {ex.Message} - re-queueing".LogDebug();
                EnqueuePending(serializedBatch);
            }
        }

        /// <summary>
        /// Replay all queued batches through <see cref="PublishOrQueue"/> with
        /// bounded in-flight concurrency capped at <see cref="DrainConcurrencyCap"/>.
        /// Without the cap, draining the full pending queue on reconnect fires
        /// every batch into client.InvokeAsync simultaneously and overloads the
        /// SignalR server, triggering another disconnect and re-storming.
        ///
        /// PublishOrQueue's fault continuation re-enqueues on failure — if
        /// the transport drops mid-drain the in-flight batch lands back in
        /// the queue from there; the next reconnect picks it up.
        /// </summary>
        private void DrainPendingPublishes(HubConnection client)
        {
            if (_pendingPublishes.IsEmpty) return;
            var dropped = Interlocked.Exchange(ref _droppedPending, 0);
            if (dropped > 0)
                $"SignalRRxnManagerBridge: dropped {dropped} pending publishes (offline-buffer cap {PendingPublishesCap}) — those events are lost".LogDebug();

            // Snapshot the queue length up-front: PublishOrQueue may re-enqueue
            // failures, and we don't want to spin forever draining-and-requeuing
            // the same batch in a single call.
            var toDrain = _pendingPublishes.Count;
            DrainPendingPublishesAsync(client, toDrain);
        }

        private async void DrainPendingPublishesAsync(HubConnection client, int toDrain)
        {
            var drained = 0;
            var inFlight = new System.Collections.Generic.List<Task>();
            for (var i = 0; i < toDrain; i++)
            {
                if (client.State != HubConnectionState.Connected) break;
                if (!_pendingPublishes.TryDequeue(out var batch)) break;

                // Wait for a slot to free up before firing the next batch.
                while (inFlight.Count >= DrainConcurrencyCap)
                {
                    var done = await Task.WhenAny(inFlight).ConfigureAwait(false);
                    inFlight.Remove(done);
                    if (client.State != HubConnectionState.Connected) { EnqueuePending(batch); return; }
                }

                inFlight.Add(InvokeBatchAsync(client, batch));
                drained++;
            }

            // Wait for the trailing in-flight invocations to settle so the
            // logged count reflects the actual drain, not the dispatch.
            try { await Task.WhenAll(inFlight).ConfigureAwait(false); } catch { /* per-batch errors handled in InvokeBatchAsync */ }

            if (drained > 0)
                $"SignalRRxnManagerBridge: drained {drained} pending publishes after reconnect".LogDebug();
        }

        private async Task InvokeBatchAsync(HubConnection client, object[] serializedBatch)
        {
            try
            {
                if (client.State != HubConnectionState.Connected)
                {
                    EnqueuePending(serializedBatch);
                    return;
                }
                await client.InvokeAsync("PublishBatch", (object)serializedBatch).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                $"PublishBatch (drain) failed: {ex.GetType().Name}: {ex.Message} - re-queueing".LogDebug();
                EnqueuePending(serializedBatch);
            }
        }
    }
}
