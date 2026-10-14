using System;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Rxns.DDD.Commanding;
using Rxns.Health;
using Rxns.Hosting;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns.Redis
{
    /// <summary>
    /// Worker-side counterpart to <see cref="RedisEventHub"/>. Wired into
    /// the worker's lifecycle by <see cref="RedisTransportModule"/>. Replaces
    /// what the SignalR <c>SignalRRxnManagerBridge</c> does under SignalR mode
    /// (subscribe to "Subscribe" messages, dispatch to local bus, send
    /// heartbeats):
    ///
    ///   1. Subscribes to the host-supplied routed-cmds Redis stream.
    ///   2. Filters incoming <see cref="RoutedEnvelope"/>s by route - only
    ///      messages targeting this worker (or Broadcast=true) are dispatched.
    ///   3. Dispatches the envelope's payload to the local IRxnManager so any
    ///      handler subscribed to the type fires (same shape as a SignalR
    ///      "Subscribe" callback would).
    ///   4. Publishes <see cref="WorkerRouteRegistered"/> on start so the
    ///      hub's <see cref="RedisEventHub"/> learns the route -> clientId
    ///      mapping. Replaces SignalR's RegisterAsService Hub method call.
    ///   5. Republishes <see cref="WorkerHeartbeat"/> on an interval derived from
    ///      the fleet size, so the arena's view of who exists stays current and a
    ///      respawned worker re-registers itself. NOT a keepalive: the hub has no
    ///      stale-route sweep, so a route survives whether or not this arrives.
    ///   6. Publishes <see cref="WorkerRouteRemoved"/> on Dispose for
    ///      graceful unregistration.
    ///
    /// Built on top of <see cref="RedisStreamBackingChannel{T}"/> for the
    /// routed-cmds stream + the worker's existing <c>IAppStatusServiceClient</c>
    /// for the typed-events stream (registration / heartbeat are typed events).
    /// </summary>
    public class RedisRoutedCmdConsumer : ReportsStatus, IRxnService, IDisposable
    {
        // First heartbeat fires fast (doubles as the route-registration message
        // - hub's RedisEventHub auto-registers any heartbeat with an unknown
        // route). The 200ms delay gives RedisAppStatusServiceClient's publish
        // chain a moment to warm up before the first message lands; longer
        // delays just slow boot, shorter ones risk the publish silently
        // dropping pre-warmup.
        private static readonly TimeSpan FirstHeartbeatDelay = TimeSpan.FromMilliseconds(200);

        /// <summary>
        /// How often a worker republishes its heartbeat, derived from the fleet size rather than
        /// fixed. See <see cref="IntervalForFleet"/>.
        ///
        /// <para>This was a hard 5s, justified in a comment by stale-route detection - "hub evicts
        /// after 20s of silence". That eviction no longer exists: RedisEventHub removed its
        /// stale-route sweep deliberately, because heartbeats published every 5s were observed
        /// ARRIVING at 22-25s with a 374s tail when the arena's consumer fell behind, so healthy
        /// workers were evicted in their hundreds and dispatches could not land. Routes now live
        /// until a heartbeat replaces them.</para>
        ///
        /// <para>So nothing depends on 5s any more. What the heartbeat still does is register a
        /// route on its first arrival, re-register a respawned worker whose clientId changed, and
        /// keep the arena's view of the fleet current - none of which need to happen twelve times a
        /// minute per machine.</para>
        /// </summary>
        public static TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Heartbeats per second the arena is expected to absorb, whatever the fleet size.
        ///
        /// <para>The interval is derived from this rather than the other way round, because the
        /// arena's capacity is the real constraint and fleet size is not. A fixed interval makes
        /// offered load grow with every machine added - at 100 workers a 5s cadence offers 20/s,
        /// which is the shape that left an arena unable to answer anyone.</para>
        ///
        /// <para>Deliberately a rate the arena is MEASURED to sustain, not a guess - see
        /// UploadUnderHeartbeatLoadBehaviour and ArenaHeartbeatLoadBehaviour, which exist to put a
        /// number here.</para>
        /// </summary>
        public static double TargetHeartbeatsPerSecond = 10.0;

        /// <summary>The fastest a worker will ever be asked to beat, however small the fleet.</summary>
        public static TimeSpan MinHeartbeatInterval = TimeSpan.FromSeconds(5);

        /// <summary>
        /// The interval that offers <see cref="TargetHeartbeatsPerSecond"/> across a fleet of the
        /// given size: interval = fleet / target.
        ///
        /// <para>Ten workers against a target of ten a second beat once a second - so the floor
        /// holds them at the configured minimum instead. A hundred workers beat every ten seconds,
        /// a thousand every hundred. The arena sees the same offered rate in each case, which is
        /// the point: adding machines adds capacity, not load on the thing coordinating them.</para>
        ///
        /// <para>Pure, so the law can be checked without a fleet.</para>
        /// </summary>
        public static TimeSpan IntervalForFleet(int fleetSize, double targetPerSecond, TimeSpan floor)
        {
            // NaN and infinity before the comparison, because NaN fails every comparison silently
            // and would reach TimeSpan.FromSeconds, which throws. A heartbeat interval is not worth
            // an exception on a worker's startup path: an unreadable target means fall back.
            if (double.IsNaN(targetPerSecond) || double.IsInfinity(targetPerSecond)) return floor;
            if (targetPerSecond <= 0) return floor;
            if (fleetSize <= 0) return floor;

            var seconds = fleetSize / targetPerSecond;
            if (double.IsNaN(seconds) || double.IsInfinity(seconds)) return floor;

            var derived = TimeSpan.FromSeconds(seconds);

            return derived < floor ? floor : derived;
        }

        private readonly RedisStreamBackingChannel<IRxn> _channel;
        private readonly IRxnManager<IRxn> _localBus;
        private readonly IAppStatusServiceClient _appStatus;
        private readonly IResolveTypes _resolver;
        private readonly string _route;
        private readonly string _clientId;
        private readonly string _routedCmdsStream;
        private CompositeDisposable _resources = new();

        public RedisRoutedCmdConsumer(string redisConnectionString,
                                       string route,
                                       IRxnManager<IRxn> localBus,
                                       IAppStatusServiceClient appStatus,
                                       IResolveTypes resolver,
                                       string routedCmdsStream,
                                       string consumerGroupPrefix)
        {
            _route = route;
            _clientId = $"{Environment.MachineName}-{Guid.NewGuid():N}".Substring(0, 24);
            _localBus = localBus;
            _appStatus = appStatus;
            _resolver = resolver;
            _routedCmdsStream = routedCmdsStream;
            // Per-route consumer group so each worker has its own delivery cursor
            // on the routed-cmds stream. Workers in the same group would share
            // work, which is wrong here - we want every worker to see every
            // envelope and filter locally.
            _channel = new RedisStreamBackingChannel<IRxn>(
                redisConnectionString,
                routedCmdsStream,
                $"{consumerGroupPrefix}{_route.Replace('\\', '_')}");

            OnInformation("RedisRoutedCmdConsumer: route='{0}' clientId='{1}' stream='{2}'",
                _route, _clientId, routedCmdsStream);
        }

        // IRxnService - Start is invoked by PostBuildRxnServiceCreator after
        // the container is built, so we don't need to manually trigger
        // resolution of this class. AsImplementedInterfaces in the DI
        // registration picks up IRxnService and Rxns iterates them at boot.
        public IObservable<CommandResult> Start(string from = null, string options = null)
        {
            // Inbound: filter envelopes addressed to this worker, then
            // dispatch payload to local bus.
            //
            // Filter precedence: Broadcast > Route > ClientId.
            //
            // Route-first is the in-flight reconnect fix (catalogued open
            // 2026-05-04 from phase 7n3): a cmd dispatched in the ~700ms
            // window between worker process death and the hub's heartbeat-
            // driven _routes clientId-update carries the DEAD consumer's
            // clientId in its envelope. The respawned consumer (this one)
            // would otherwise skip it because its _clientId is freshly
            // generated. Matching on route — which is stable across
            // respawns under the same systemName (e.g. `as W2`) — accepts
            // the in-flight cmd and dispatches it.
            //
            // ClientId fallback covers two cases:
            //   1. Pre-route-field hub builds (RoutedEnvelope.Route null).
            //   2. CommandResult-back-to-source publishes for clients with
            //      no registered route (e.g. one-shot browser cmds), where
            //      hub can't reverse-lookup a route from clientId.
            var inbound = _channel.Setup(new IdentityDelivery())
                .OfType<RoutedEnvelope>()
                .Where(e => ShouldDispatch(e, _route, _clientId))
                .Subscribe(envelope =>
                {
                    try
                    {
                        Dispatch(envelope);
                    }
                    catch (Exception ex)
                    {
                        OnError(new Exception($"RedisRoutedCmdConsumer dispatch failed for method='{envelope.Method}': {ex.Message}", ex));
                    }
                });
            _resources.Add(inbound);

            // Heartbeat doubles as route-registration: the hub's
            // RedisEventHub auto-registers on first heartbeat for an unknown
            // route, so a separate WorkerRouteRegistered event was redundant
            // and racy at boot - it fired before RedisAppStatusServiceClient's
            // publish chain was fully warm and silently dropped under
            // concurrent worker spawn. First heartbeat at 200ms means the
            // hub learns the route in ~250ms instead of 5s.
            // Rescheduled each beat rather than fixed by Observable.Timer, so a change to
            // HeartbeatInterval takes effect on the next one.
            //
            // Timer captures its period at subscribe time, so the interval could only ever be
            // changed by tearing the subscription down and rebuilding it - and the thing that wants
            // to change it is the arena, mid-run, as the fleet grows. Reading the field each beat
            // means there is nothing to signal and nothing to resubscribe: the next beat simply
            // uses the current value.
            var heartbeat = Scheduler.Default.Schedule(FirstHeartbeatDelay, Beat);
            _resources.Add(heartbeat);

            return Observable.Return(CommandResult.Success("RedisRoutedCmdConsumer started"));
        }

        /// <summary>
        /// Publishes one heartbeat, then books the next at whatever the interval currently says.
        ///
        /// <para>Errors are reported and the beat is rebooked regardless. A heartbeat that failed to
        /// publish is not a reason to stop heartbeating - the arena would then see a worker that
        /// went quiet, which is the same signal as a worker that died, and a transient publish
        /// failure is not that.</para>
        /// </summary>
        private void Beat(Action<TimeSpan> again)
        {
            try
            {
                _appStatus.Publish(new[] { (IRxn)new WorkerHeartbeat { ClientId = _clientId, Route = _route } })
                    .Until(e => OnError(new Exception("WorkerHeartbeat publish failed", e)));
            }
            catch (Exception ex)
            {
                OnError(new Exception("WorkerHeartbeat publish threw", ex));
            }

            var next = HeartbeatInterval;
            if (next <= TimeSpan.Zero) next = MinHeartbeatInterval;

            again(next);
        }

        public IObservable<CommandResult> Stop(string from = null)
        {
            Dispose();
            return Observable.Return(CommandResult.Success("RedisRoutedCmdConsumer stopped"));
        }

        public IObservable<CommandResult> Setup() =>
            Observable.Return(CommandResult.Success("RedisRoutedCmdConsumer setup"));

        // Filter predicate exposed as a static so the unit test exercises the
        // exact production logic (not a copy that could drift). Returns true
        // when the envelope is broadcast, route-matches, or clientId-matches.
        public static bool ShouldDispatch(RoutedEnvelope e, string workerRoute, string workerClientId) =>
            e != null && (e.Broadcast || RouteMatches(e, workerRoute) || ClientIdMatches(e, workerClientId));

        // Route on the envelope vs the worker's stable route. Case-insensitive
        // because RouteExtensions.GetRoute callsites have historically drifted
        // between `notenant\W1` and `NoTenant\W1` casings (defensive against
        // a regression).
        public static bool RouteMatches(RoutedEnvelope e, string workerRoute) =>
            e != null &&
            !string.IsNullOrEmpty(e.Route) &&
            !string.IsNullOrEmpty(workerRoute) &&
            string.Equals(e.Route, workerRoute, StringComparison.OrdinalIgnoreCase);

        // ClientId fallback path - matches the legacy hub envelope shape
        // (no Route field) and CommandResult back-routing where the hub
        // couldn't resolve a route.
        public static bool ClientIdMatches(RoutedEnvelope e, string workerClientId) =>
            e != null &&
            !string.IsNullOrEmpty(e.ClientId) &&
            !string.IsNullOrEmpty(workerClientId) &&
            string.Equals(e.ClientId, workerClientId, StringComparison.Ordinal);

        private void Dispatch(RoutedEnvelope envelope)
        {
            // The envelope's Payload is what was passed as the SignalR method
            // argument. For "Subscribe" (the canonical command-routing call),
            // it's a serialized IRxn that the local bus should publish so any
            // local handler subscribed to that type fires - same shape as
            // SignalR's "Subscribe" client-side callback.
            if (envelope.Method == "Subscribe" && !string.IsNullOrEmpty(envelope.Payload))
            {
                var deserialised = envelope.Payload.Deserialise(envelope.Payload.GetTypeFromJson(_resolver)) as IRxn;
                if (deserialised != null)
                    _localBus.Publish(deserialised).Until(e => OnError(new Exception("Local re-publish failed", e)));
            }
            else
            {
                OnVerbose("RedisRoutedCmdConsumer: ignoring envelope method='{0}' (not yet handled)", envelope.Method);
            }
        }

        public void Dispose()
        {
            try
            {
                _appStatus.Publish(new[] { (IRxn)new WorkerRouteRemoved { Route = _route } })
                    .Until(e => OnError(new Exception("WorkerRouteRemoved publish failed", e)));
            }
            catch (Exception ex)
            {
                OnError(new Exception("RedisRoutedCmdConsumer.Dispose: WorkerRouteRemoved best-effort failed", ex));
            }
            _resources?.Dispose();
            _channel?.Dispose();
        }

        private class IdentityDelivery : IDeliveryScheme<IRxn>
        {
            public void Deliver(IRxn @event, Action<IRxn> postBox) => postBox(@event);
            public IObservable<IRxn> Deliver(IRxn @event, Func<IRxn, IObservable<IRxn>> postBox) => postBox(@event);
        }
    }
}
