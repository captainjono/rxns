using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Rxns.Cloud;
using Rxns.Collections;
using Rxns.DDD.Commanding;
using Rxns.Hosting;
using Rxns.Hosting.Updates;
using Rxns.Interfaces;
using Rxns.Logging;
using Rxns.Microservices;

namespace Rxns.Redis
{
    /// <summary>
    /// Redis-backed implementation of <see cref="IEventHub"/>. Symmetric
    /// counterpart to the SignalR-backed <c>EventsHub</c> (in
    /// Rxns.WebApiNET5). Wired in by <see cref="RedisTransportModule"/> on
    /// arena hosts; consumers binding to <see cref="IEventHub"/> /
    /// <see cref="IAppCmdManager"/> resolve to this instance instead of the
    /// SignalR hub, so cluster-internal command routing rides Redis Streams
    /// instead of SignalR.
    ///
    /// Scope of this scaffold:
    ///   - Route map (route -> clientId) is in-memory per arena. Single-
    ///     arena deployments only; multi-arena would need the map in
    ///     Redis.
    ///   - <see cref="SendToClientAsync"/> / <see cref="BroadcastAsync"/>
    ///     publish a <see cref="RoutedEnvelope"/> to the host-supplied
    ///     routed-cmds stream. Worker-side consumer filters by
    ///     <c>clientId</c>.
    ///   - <see cref="ClientLifecycle"/> is heartbeat-driven: routes are
    ///     auto-registered on first heartbeat, evicted after a stale-route
    ///     timeout.
    ///
    /// Logs prefix lines with "RedisEventHub:" so they're easy to grep
    /// against the SignalR equivalent during a lossless A/B.
    /// </summary>
    public class RedisEventHub : ReportsStatus, IEventHub, IRxnService, IDisposable
    {
        // Stream the arena publishes routed commands to. Workers in lossless
        // mode subscribe and filter by ClientId. Separate from the typed-events
        // stream (UnitTestResult, AppHeartbeat, ...) so command-routing traffic
        // doesn't interleave with telemetry. Concrete name is host-supplied.
        public string RoutedCmdsStream { get; }

        private readonly RedisStreamBackingChannel<IRxn> _channel;
        private readonly IAppStatusStore _appStatusStore;
        private readonly IAppCmdManager _appCmds;   // ← cmd-tracking + multi-channel routing (post 7n4 refactor)
        private readonly IRxnManager<IRxn> _rxnManager;
        private readonly IResolveTypes _resolver;

        // route -> clientId. Same shape as the SignalR hub's _routes so
        // consumers iterating Routes don't see a different surface.
        private readonly IDictionary<string, string> _routes =
            new UseConcurrentReliableOpsWhenCastToIDictionary<string, string>(
                new ConcurrentDictionary<string, string>());

        private readonly Subject<ClientLifecycleEvent> _lifecycle = new();

        // Stale-route detection: how long without a WorkerHeartbeat before we
        // evict + emit Connected=false. Aligns roughly with SignalR's KeepAlive
        // (20s server / 30s client) to keep the failure-detection window
        // comparable across modes.
        private static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(5);

        // Route -> last heartbeat seen. Sweep evicts entries older than
        // HeartbeatTimeout.
        private readonly ConcurrentDictionary<string, DateTime> _lastHeartbeat = new();

        private CompositeDisposable _resources = new();

        public RedisEventHub(string redisConnectionString,
                             IAppStatusStore appStatusStore,
                             IAppCmdManager appCmds,
                             IRxnManager<IRxn> rxnManager,
                             IResolveTypes resolver,
                             string routedCmdsStream,
                             string consumerGroup)
        {
            _appStatusStore = appStatusStore;
            _appCmds = appCmds;
            _rxnManager = rxnManager;
            _resolver = resolver;
            RoutedCmdsStream = routedCmdsStream;
            _channel = new RedisStreamBackingChannel<IRxn>(
                redisConnectionString,
                RoutedCmdsStream,
                consumerGroup);

            OnInformation("RedisEventHub: stream='{0}' group='{1}'", RoutedCmdsStream, consumerGroup);
            // Phase 7n4 refactor: register THIS channel with the cmd manager
            // so its Add() can dispatch via SendToClientAsync when a route
            // lives here. Cross-channel routing decisions (cmd entered via
            // SignalR, result arrives here) work because tracking is
            // co-located on IAppCmdManager.
            _appCmds.RegisterChannel(this);

            // Subscribe to worker heartbeat / removal events on the local bus.
            // They arrive here because the arena's RedisAppStatusServiceClient.Incoming
            // is wired into the local bus by the AppStatusBackingChannel -> any
            // IRxn workers publish via the typed-events stream lands locally.
            //
            // Note: there's no separate WorkerRouteRegistered subscription -
            // the heartbeat handler below auto-registers any heartbeat for an
            // unknown route, so registration and liveness ride one event type.
            // An explicit "I'm joining" event was racy at boot (workers could
            // emit it before their publish chain was warm) and redundant
            // because heartbeats start within 200ms of Start.
            _resources.Add(_rxnManager.CreateSubscription<WorkerRouteRemoved>()
                .Subscribe(ev =>
                {
                    if (ev?.Route == null) return;
                    OnVerbose("RedisEventHub: WorkerRouteRemoved route='{0}'", ev.Route);
                    var rootRoute = ev.Route.AsRootRoute();
                    _lastHeartbeat.TryRemove(rootRoute, out _);
                    RemoveRoute(ev.Route);
                }));

            _resources.Add(_rxnManager.CreateSubscription<WorkerHeartbeat>()
                .Subscribe(ev =>
                {
                    if (ev?.Route == null) return;
                    var rootRoute = ev.Route.AsRootRoute();
                    _lastHeartbeat[rootRoute] = DateTime.UtcNow;
                    // Heartbeat = registration: register on first sighting,
                    // refresh _lastHeartbeat on every subsequent tick. The
                    // first heartbeat is what populates _routes - arrives
                    // ~200ms after worker boot. Self-healing: if the arena
                    // restarts while a worker stays alive, the next heartbeat
                    // re-registers the route automatically.
                    //
                    // ClientId-change detection (phase 7n3 W1_1 symptom): a
                    // worker process can die + respawn with the SAME route
                    // (`as W2`) but a NEW SignalR/Redis-stream consumerId.
                    // Without this branch, the existing-route check above
                    // would short-circuit the re-register and routed cmds
                    // would keep going to the dead consumer's stream
                    // entries — silently dropped. Update _routes' clientId
                    // when a heartbeat shows a different one.
                    var newClientId = ev.ClientId ?? ev.Route;
                    if (_routes.TryGetValue(rootRoute, out var existingClientId))
                    {
                        if (!string.Equals(existingClientId, newClientId, StringComparison.OrdinalIgnoreCase))
                        {
                            OnInformation("RedisEventHub: heartbeat for known route '{0}' carries new clientId '{1}' (was '{2}') - updating route", rootRoute, newClientId, existingClientId);
                            _routes.AddOrReplace(rootRoute, newClientId);
                        }
                    }
                    else
                    {
                        OnInformation("RedisEventHub: heartbeat for unknown route '{0}' - registering", rootRoute);
                        RegisterRoute(newClientId, ev.Route);
                    }
                }));

            // Periodic sweep: evict routes whose last heartbeat is older than
            // HeartbeatTimeout. Sample at SweepInterval so we don't spin a
            // dedicated timer thread - rides Rx's default scheduler.
            _resources.Add(Observable.Interval(SweepInterval)
                .Subscribe(_ => SweepStaleRoutes()));
        }

        private void SweepStaleRoutes()
        {
            try
            {
                var now = DateTime.UtcNow;
                // Filter on the snapshot of (key, ageSeconds) so we don't read
                // the dictionary again later — the previous code re-indexed
                // _lastHeartbeat[route] at the OnVerbose line, which threw
                // KeyNotFoundException whenever a concurrent registration
                // (or a previous sweep iteration mid-foreach) had already
                // removed the entry. The exception aborted the sweep BEFORE
                // the TryRemove + RemoveRoute steps ran, so the next sweep
                // tick saw the same stale entry and threw again — every 5
                // seconds forever, flooding /systemstatus/log with
                // "SweepStaleRoutes failed". Phase 7n3 caught this after a
                // worker process was killed.
                var stale = _lastHeartbeat
                    .Where(kv => now - kv.Value > HeartbeatTimeout)
                    .Select(kv => new { Route = kv.Key, AgeMs = (now - kv.Value).TotalMilliseconds })
                    .ToList();
                foreach (var s in stale)
                {
                    // Atomic remove-with-value: skip if a concurrent thread
                    // already evicted. Idempotent across overlapping sweeps.
                    if (!_lastHeartbeat.TryRemove(s.Route, out _))
                        continue;
                    OnVerbose("RedisEventHub: sweep evicting stale route '{0}' (last heartbeat {1:0}ms ago)", s.Route, s.AgeMs);
                    try { RemoveRoute(s.Route); }
                    catch (Exception removeEx)
                    {
                        OnError(new Exception($"RedisEventHub: RemoveRoute('{s.Route}') in sweep failed", removeEx));
                    }
                }
            }
            catch (Exception ex)
            {
                OnError(new Exception("RedisEventHub.SweepStaleRoutes failed", ex));
            }
        }

        // ── IEventHub ─────────────────────────────────────────────────────────

        public IReadOnlyDictionary<string, string> Routes =>
            _routes.ToDictionary(kv => kv.Key, kv => kv.Value);

        public void RegisterRoute(string clientId, string route)
        {
            var rootRoute = route.AsRootRoute();
            OnInformation("RedisEventHub: register route '{0}' -> '{1}'", rootRoute, clientId);
            _routes.AddOrReplace(rootRoute, clientId);
            _lifecycle.OnNext(new ClientLifecycleEvent { ClientId = clientId, Connected = true });
        }

        public void RemoveRoute(string route)
        {
            var rootRoute = route.AsRootRoute();
            OnInformation("RedisEventHub: remove route '{0}'", rootRoute);
            if (_routes.TryGetValue(rootRoute, out var clientId))
            {
                _routes.Remove(rootRoute);
                _lifecycle.OnNext(new ClientLifecycleEvent { ClientId = clientId, Connected = false });
            }
        }

        public void PublishFromClient(string clientId, string serializedEvent)
        {
            var deserialized = serializedEvent.Deserialise(serializedEvent.GetTypeFromJson(_resolver)) as IRxn;
            _rxnManager.Publish(deserialized).Until(e => OnError(new Exception("Failed to publish from remote client! ", e)));

            // Track originator on the shared cmd manager. Phase 7n4 refactor:
            // tracking lives on IAppCmdManager (singleton), not in this hub's
            // private dicts — so cmds entering via SignalR + results arriving
            // here both resolve correctly.
            if (deserialized is IUniqueRxn q && deserialized is not CommandResult)
            {
                var publisherRoute = LookupRouteByClientId(clientId);
                if (publisherRoute != null)
                    _appCmds.TrackOriginator(q.Id, publisherRoute);
            }

            // Telemetry must not be forwarded - see SignalR hub Publish for the
            // full echo-loop reasoning.
            if (deserialized is not CommandResult cr) return;

            if (string.IsNullOrEmpty(cr.InResponseTo)) return;

            // Resolve via shared store. If the originator's route lives on
            // THIS channel, deliver via Redis stream directly. Otherwise hand
            // off to IAppCmdManager.Add which iterates registered channels
            // (including this one) to find the right transport.
            var targetRoute = _appCmds.ResolveAndForgetOriginator(cr.InResponseTo);
            if (string.IsNullOrEmpty(targetRoute))
            {
                OnInformation("RedisEventHub: NO ROUTE for InResponseTo={0} type={1}", cr.InResponseTo, cr.GetType().Name);
                return;
            }

            if (_routes.TryGetValue(targetRoute, out var targetClientId))
            {
                if (targetClientId == clientId) return;   // would echo to self
                PublishEnvelope(targetClientId, targetRoute, "Subscribe", serializedEvent, broadcast: false);
            }
            else
            {
                // Cross-channel: SignalR or another transport owns this route.
                _appCmds.Add(new RxnQuestion
                {
                    Destination = targetRoute,
                    Options     = serializedEvent,
                    Id          = Guid.NewGuid().ToString()
                });
            }
        }

        public Task SendToClientAsync(string clientId, string method, object payload)
        {
            // Public IEventHub entry point - signature stable. Routes resolved
            // best-effort via reverse lookup; callers that already know the
            // route (Add) use PublishEnvelope directly to avoid the lookup.
            PublishEnvelope(clientId, LookupRouteByClientId(clientId), method, payload, broadcast: false);
            return Task.CompletedTask;
        }

        public Task BroadcastAsync(string method, object payload)
        {
            PublishEnvelope(clientId: string.Empty, route: null, method, payload, broadcast: true);
            return Task.CompletedTask;
        }

        // Centralised envelope build + publish so the route-carry is consistent
        // across SendToClientAsync, Add, BroadcastAsync, and the
        // CommandResult back-routing path. Route may be null when unknown -
        // consumer-side filter falls back to clientId match.
        private void PublishEnvelope(string clientId, string route, string method, object payload, bool broadcast)
        {
            var envelope = new RoutedEnvelope
            {
                ClientId  = clientId ?? string.Empty,
                Route     = route,
                Method    = method,
                Payload   = payload?.ToString() ?? string.Empty,
                Broadcast = broadcast
            };
            _channel.Publish(envelope);
        }

        // Reverse-lookup the route for a given clientId. O(routes) but the
        // route map is small (one entry per worker + orchestrator). Returns
        // null when not registered (e.g. one-shot browser publish).
        private string LookupRouteByClientId(string clientId)
        {
            if (string.IsNullOrEmpty(clientId)) return null;
            foreach (var kv in _routes)
                if (string.Equals(kv.Value, clientId, StringComparison.OrdinalIgnoreCase))
                    return kv.Key;
            return null;
        }

        public IObservable<ClientLifecycleEvent> ClientLifecycle => _lifecycle;

        // ── IRxnService ──────────────────────────────────────────────────────
        // Implemented purely so PostBuildRxnServiceCreator activates this class
        // at boot. Without that the hub is registered-but-lazy: if no consumer
        // resolves IEventHub/IAppCmdManager during the static DI graph walk,
        // Autofac never constructs the instance, so nothing subscribes to the
        // local bus and the _routes map stays permanently empty.
        // Constructor already wired all the subscriptions; Start is a no-op
        // beyond confirming the service is alive.
        public IObservable<CommandResult> Start(string from = null, string options = null) =>
            Observable.Return(CommandResult.Success("RedisEventHub started"));

        public IObservable<CommandResult> Stop(string from = null) =>
            Observable.Return(CommandResult.Success("RedisEventHub stopped"));

        public IObservable<CommandResult> Setup() =>
            Observable.Return(CommandResult.Success("RedisEventHub setup"));

        // ── IEventHub-only surface (was IAppCmdManager) ───────────────────────
        // Add(IRxnQuestion) and FlushCommands(route) live on IAppCmdManager
        // (RoutableAppCmdManager singleton). Phase 7n4 refactor: cmd routing
        // is channel-agnostic, owned by the manager. This hub exposes
        // SendToClientAsync which the manager calls to dispatch via the Redis
        // routed-cmds stream.
        //
        // Manager calls SendToClientAsync(clientId, "Subscribe", payload).
        // We route via PublishEnvelope so the consumer-side filter works
        // (route-first matching protects against the in-flight reconnect gap;
        // see RedisRoutedCmdConsumer.ShouldDispatch).

        public bool HasRoute(string route)
        {
            if (string.IsNullOrEmpty(route)) return false;
            return _routes.ContainsKey(route.AsRootRoute());
        }

        public void Dispose()
        {
            _resources?.Dispose();
            _channel?.Dispose();
        }
    }

    /// <summary>
    /// Envelope published to the host-supplied routed-cmds Redis stream by
    /// <see cref="RedisEventHub"/>. Carries a SignalR-equivalent command
    /// invocation (method + payload) plus the target clientId (or empty +
    /// Broadcast=true for fan-out).
    ///
    /// Workers consuming the stream filter by <see cref="Route"/> first and
    /// fall back to <see cref="ClientId"/>. Filtering by route closes the
    /// ~700ms in-flight gap between worker death and the arena's heartbeat-
    /// driven clientId update — a cmd dispatched in that window carries the
    /// dead clientId, but the route still matches the live consumer that
    /// respawned under the same systemName. Pre-route-field envelopes
    /// (older arena builds) leave Route null; consumers fall back to
    /// clientId match for back-compat.
    /// </summary>
    public class RoutedEnvelope : IRxn
    {
        public string ClientId { get; set; }
        public string Route { get; set; }
        public string Method { get; set; }
        public string Payload { get; set; }
        public bool Broadcast { get; set; }
    }
}
