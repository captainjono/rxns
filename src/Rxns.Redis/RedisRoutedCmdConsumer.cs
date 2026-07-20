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
    ///      arena's <see cref="RedisEventHub"/> learns the route -> clientId
    ///      mapping. Replaces SignalR's RegisterAsService Hub method call.
    ///   5. Heartbeats every ~5 s via <see cref="WorkerHeartbeat"/> so the
    ///      arena can detect disconnects passively.
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
        // - arena's RedisEventHub auto-registers any heartbeat with an unknown
        // route). The 200ms delay gives RedisAppStatusServiceClient's publish
        // chain a moment to warm up before the first message lands; longer
        // delays just slow boot, shorter ones risk the publish silently
        // dropping pre-warmup. Steady-state cadence stays at 5s for
        // stale-route detection (arena evicts after 20s of silence).
        private static readonly TimeSpan FirstHeartbeatDelay = TimeSpan.FromMilliseconds(200);
        private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(5);

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
            // window between worker process death and the arena's heartbeat-
            // driven _routes clientId-update carries the DEAD consumer's
            // clientId in its envelope. The respawned consumer (this one)
            // would otherwise skip it because its _clientId is freshly
            // generated. Matching on route — which is stable across
            // respawns under the same systemName (e.g. `as W2`) — accepts
            // the in-flight cmd and dispatches it.
            //
            // ClientId fallback covers two cases:
            //   1. Pre-route-field arena builds (RoutedEnvelope.Route null).
            //   2. CommandResult-back-to-source publishes for clients with
            //      no registered route (e.g. one-shot browser cmds), where
            //      arena can't reverse-lookup a route from clientId.
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

            // Heartbeat doubles as route-registration: the arena's
            // RedisEventHub auto-registers on first heartbeat for an unknown
            // route, so a separate WorkerRouteRegistered event was redundant
            // and racy at boot - it fired before RedisAppStatusServiceClient's
            // publish chain was fully warm and silently dropped under
            // concurrent worker spawn. First heartbeat at 200ms means the
            // arena learns the route in ~250ms instead of 5s.
            var heartbeat = Observable.Timer(FirstHeartbeatDelay, HeartbeatInterval)
                .Subscribe(_ =>
                {
                    _appStatus.Publish(new[] { (IRxn)new WorkerHeartbeat { ClientId = _clientId, Route = _route } })
                        .Until(e => OnError(new Exception("WorkerHeartbeat publish failed", e)));
                });
            _resources.Add(heartbeat);

            return Observable.Return(CommandResult.Success("RedisRoutedCmdConsumer started"));
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

        // ClientId fallback path - matches the legacy arena envelope shape
        // (no Route field) and CommandResult back-routing where the arena
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
