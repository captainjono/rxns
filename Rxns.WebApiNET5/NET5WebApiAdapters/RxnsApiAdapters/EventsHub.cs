using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Security.Principal;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Rxns;
using Rxns.Cloud;
using Rxns.Collections;
using Rxns.DDD;
using Rxns.DDD.Commanding;
using Rxns.Health.AppStatus;
using Rxns.Hosting;
using Rxns.Hosting.Updates;
using Rxns.Interfaces;
using Rxns.Logging;
using Rxns.Microservices;

namespace Rxns.WebApiNET5.NET5WebApiAdapters.RxnsApiAdapters
{
    public interface IHubClientContext
    {
        string ConntectionId { get; }
        IPrincipal User { get; }
    }

    public class SignalRHubClientContext : IHubClientContext
    {
        private readonly HubCallerContext _context;

        public SignalRHubClientContext(HubCallerContext context)
        {
            _context = context;
        }

        public string ConntectionId { get { return _context.ConnectionId; } }
        public IPrincipal User { get { return _context.User; } }
    }

    public interface IAppEventManagerBridge
    {
        Task RegisterAsService(string route);
        Task Publish(string @event);
        Task Subscribe(string @event);
    }

    public interface IAppStatusHub : IAppEventManagerBridge
    {
        Task StatusUpdatesSubscribe(IEnumerable<object> statuses);
        Task RemoteCommand(RxnQuestion cmd);
        Task StatusInitialSubscribe(IEnumerable<object> statuses);
    }

    public class RemoteEventReceived : IRxn
    {
        public string Message { get; set; }
        public string Tenant { get; set; }
        public string Destination { get; set; }
    }

    /// <summary>
    /// SignalR-backed cluster event hub. Owns the route map, command-source
    /// tracking, and IHubContext-driven send paths. Implements the abstract
    /// <see cref="IEventHub"/> so consumers can be tested against a stub and
    /// the lossless Redis transport can be plugged in by registering a
    /// <c>RedisEventHub</c> for IEventHub instead of this class.
    ///
    /// Note: this class IS the SignalR Hub<IAppStatusHub>. State (_routes,
    /// _commandSources, statusStore subscription) lives on this single
    /// singleton instance - extracting it into a separate class regresses
    /// AppStatusV2 / RemoteShell / dispatch tests because the IHubContext
    /// coupling and the per-invocation HubCallerContext sit on this type.
    /// IEventHub is the contract; SignalREventHub-the-name == this class.
    /// </summary>
    //[Authorize]
    public class EventsHub : ReportsStatusEventsHub<IAppStatusHub>, IRxnLogger, IEventHub
    {
        private readonly ISystemStatusStore _statusStore;
        private readonly IHubContext<EventsHub> _context;
        private readonly IAppStatusStore _appStatusStore;
        private readonly IAppCmdManager _appCmds;   // ← cmd-tracking + multi-channel routing (post 7n4 refactor)
        private readonly IRxnManager<IRxn> _rxnManager;
        private IRxnAppInfo _systeminfo;
        private IDictionary<string, string> _routes = new UseConcurrentReliableOpsWhenCastToIDictionary<string,string>(new ConcurrentDictionary<string, string>());
        private readonly Subject<ClientLifecycleEvent> _lifecycle = new();
        private IResolveTypes _resolver;

        public new Action<LogMessage<string>> Information => info =>
        {
            LogReceived(new RemoteEventReceived()
            {
                Message = info.FromMessage().Serialise(),
                Tenant = _systeminfo.Name,
                Destination = "Everyone"
            });
        };

        public new Action<LogMessage<Exception>> Errors => error =>
        {
            LogReceived(new RemoteEventReceived
            {
                Message = error.FromMessage().Serialise(),
                Tenant = _systeminfo.Name,
                Destination = "Everyone"
            });
        };

        public EventsHub(IEnumerable<IAppContainer> containers, ISystemStatusStore statusStore, IHubContext<EventsHub> context, IAppStatusStore appStatusStore, IAppCmdManager appCmds, IRxnManager<IRxn> rxnManager, IResolveTypes resolver) //should this be a IRxnPublisher instead? does that work, not sure of lifetimes?
        {
            _statusStore = statusStore;
            _context = context;
            _appStatusStore = appStatusStore;
            _appCmds = appCmds;
            _rxnManager = rxnManager;
            _resolver = resolver;
            // Register THIS channel with the cmd manager so its Add() can
            // dispatch via SendToClientAsync when a route lives here. Phase
            // 7n4 refactor: tracking + cross-channel routing decisions live
            // on IAppCmdManager, not on the hub itself.
            _appCmds.RegisterChannel(this);

            foreach (var container in containers)
            {
                _systeminfo = container.Resolve<IRxnAppInfo>();

                container.SubscribeAll(info =>
                {
                    var si = _systeminfo;
                    LogReceived(new RemoteEventReceived()
                    {
                        Message = info.FromMessage().Serialise(),
                        Tenant = si.Name,
                        Destination = "Everyone"
                    });
                }, error =>
                {
                    var si = _systeminfo;
                    LogReceived(new RemoteEventReceived
                    {
                        Message = error.FromMessage().Serialise(),
                        Tenant = si.Name,
                        Destination = "Everyone"
                    });
                }).DisposedBy((IManageResources)this);
            }

            // Rate-limit push so the UI's SystemStatus view doesn't repaint per
            // heartbeat under load: at most one push per ~5 s, always reflecting
            // the latest state. Sample (interval-driven) not Throttle (debounce):
            // back-to-back heartbeats around the heartbeat cadence (~5 s apart)
            // would keep a Throttle timer resetting and never emit. Sample emits
            // the most recent value at each interval tick, so a steady heartbeat
            // stream still gets through. statusStore already emits only on real
            // change upstream so no DistinctUntilChanged gate needed (Count was
            // a lossy uniqueness key anyway — two distinct states can share it,
            // e.g. one system drops as another joins).
            OnInformation("EventsHub.ctor: subscribing to statusStore (updates-push path)");
            statusStore
                .Sample(TimeSpan.FromSeconds(5))
                .Subscribe(this, s =>
                {
                    _context.Clients.All.SendAsync("StatusUpdatesSubscribe", BuildStatusPayload(s));
                })
                .DisposedBy(this);

            // Local-bus -> SignalR-client return path. Under lossless mode,
            // CommandResults arrive on the local bus from Redis (worker
            // emits via RedisAppStatusServiceClient -> arena's
            // RedisInboundEventPump -> local bus) without ever passing
            // through PublishFromClient, so the per-connection routing the
            // hub does for SignalR-sourced results is bypassed and the
            // initiator never sees them. This subscription mirrors the
            // routing tail of PublishFromClient: look up the originating
            // connection in _commandSources by InResponseTo and SendAsync
            // the result back. _commandSources.TryRemove makes this
            // idempotent with PublishFromClient's own routing - whichever
            // path fires first wins; the other is a no-op.
            _rxnManager.CreateSubscription<CommandResult>()
                .Subscribe(this, RouteCommandResultToInitiator)
                .DisposedBy(this);

            // No bus-wide broadcast pump on this hub: that path used to send
            // every .Emits<>()-listed IRxn to Clients.All — including connected
            // workers / laptop targets, which then re-published onto their own
            // local bus, which their .Emits<>() forwarders pushed back to Redis,
            // which the arena consumed and re-broadcast → unbounded echo loop
            // (observed 2026-05-01: 20 AppHeartbeats in 200ms from a single
            // idle worker). Funnels for the UI:
            //   • app-status / resource-info → statusStore.Sample(5s) above,
            //     pushed via "StatusUpdatesSubscribe" (goes to all clients but
            //     workers don't have a handler for it, so no loop)
            //   • test-domain events → bfgTestArenaProgressHub (separate hub
            //     URL; workers never connect there)
            //   • route-targeted commands → Add() / RouteCommandResultToInitiator
            //     (per-connection, never broadcast)
        }

        private void RouteCommandResultToInitiator(CommandResult cr)
        {
            if (cr == null || string.IsNullOrEmpty(cr.InResponseTo)) return;
            // Cmd→originator lookup is on the shared IAppCmdManager singleton
            // (post 7n4 refactor), not in this hub's private dicts. So results
            // arriving via THIS channel for cmds dispatched via ANOTHER channel
            // still resolve correctly.
            var route = _appCmds.ResolveAndForgetOriginator(cr.InResponseTo);
            if (string.IsNullOrEmpty(route))
            {
                OnInformation("EventsHub.RouteCommandResultToInitiator: NO ROUTE for InResponseTo={0} type={1} ({2} known routes here)",
                    cr.InResponseTo, cr.GetType().Name, _routes.Count);
                return;
            }

            // Three delivery paths, in order:
            //   1. `route` is a registered route on THIS hub — look up connId
            //      and SendAsync. Standard worker/orchestrator case.
            //   2. `route` doesn't appear in _routes BUT looks like a raw
            //      SignalR connectionId (no backslash) — UI clients that
            //      never RegisterAsService'd are tracked by their connId.
            //      Try a direct SendAsync to that connection. If it's stale,
            //      SignalR drops silently (acceptable).
            //   3. `route` is real but lives on another channel — hand off
            //      via IAppCmdManager.Add for cross-channel delivery.
            // Results go out the same way commands do: through IAppCmdManager, which picks the most
            // specific route, prefers a channel that redelivers over one that forgets, and queues
            // when nothing owns the route yet.
            //
            // This used to SendAsync straight to the connection when the route was on this hub. A
            // hub send to a connection that has since dropped succeeds with no exception and no ack,
            // so the result was gone - and a lost result is worse than a lost command, because the
            // work already ran. Every rung this session finished on the worker and then hung the
            // orchestrator waiting for a result that no longer existed, which is why runs had to be
            // recovered by exporting tapes by hand.
            if (route.Contains("\\"))
            {
                _appCmds.Add(new RxnQuestion
                {
                    Destination = route,
                    Options     = cr.Serialise().ResolveAs(cr.GetType()),
                    Id          = Guid.NewGuid().ToString()
                });

                OnInformation("EventsHub.RouteCommandResultToInitiator: InResponseTo={0} type={1} route='{2}' handed to IAppCmdManager",
                    cr.InResponseTo, cr.GetType().Name, route);
            }
            else
            {
                // No backslash → likely a raw connId tracked by EventsHub.SendCommand
                // for an unregistered UI client. Best-effort SendAsync; if the
                // connection is gone, SignalR silently no-ops.
                try
                {
                    _context.Clients.Client(route).SendAsync("Subscribe", cr.Serialise().ResolveAs(cr.GetType()));
                    OnInformation("EventsHub.RouteCommandResultToInitiator: connId-fallback InResponseTo={0} type={1} → connId={2}",
                        cr.InResponseTo, cr.GetType().Name, route);
                }
                catch (Exception ex)
                {
                    OnError(new Exception($"EventsHub: failed connId-fallback for InResponseTo={cr.InResponseTo} → {route}", ex));
                }
            }
        }

        public override Task OnConnectedAsync()
        {
            this.ReportExceptions(() =>
            {
                OnInformation("EventsHub.OnConnectedAsync: {0} user={1}", Context.ConnectionId, Context.User == null ? "null" : Context.User.Identity?.Name ?? "anon");
                _lifecycle.OnNext(new ClientLifecycleEvent { ClientId = Context.ConnectionId, Connected = true });

                // Always send initial status — SendInitalStatus reads from the
                // singleton statusStore + writes to Clients.Client(connectionId);
                // it has no Context.User dependency. The previous Context.User-null
                // gate (defensive against an auth-pipeline assumption that
                // production never satisfies — SignalR sets User to a generic
                // ClaimsPrincipal at most) blanked #/appStatusV2 on every browser
                // connect because no User claim arrives without an auth pipeline
                // wired in. Phase 7n4: regression observed as "UI never displays
                // any cluster status now".
                OnVerbose("{0} connected", Context.ConnectionId);
                SendInitalStatus(_context.Clients.Client(Context.ConnectionId));
            });
            return base.OnConnectedAsync();
        }

        // Mirror the SignalR JSON protocol config (camelCase) so diagnostic logs
        // reflect what's actually on the wire, not the CLR PascalCase shape.
        private static readonly System.Text.Json.JsonSerializerOptions _wireJson = new()
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        };

        private void SendInitalStatus(IClientProxy caller)
        {
            _statusStore.FirstAsync().Subscribe(this, s =>
            {
                var payload = BuildStatusPayload(s);
                var json = System.Text.Json.JsonSerializer.Serialize(payload, _wireJson);
                OnInformation("EventsHub.SendInitalStatus: StatusInitialSubscribe push, entries={0}, wirePayload={1}", s?.Count ?? -1, json.Length > 800 ? json.Substring(0, 800) + "...TRUNCATED" : json);
                caller.SendAsync("StatusInitialSubscribe", payload);
            });
        }

        private static object[] BuildStatusPayload(Dictionary<SystemStatusEvent, object[]> s)
        {
            return s.Distinct(new TenantOnlyStatusComparer())
                .Select(x => new
                {
                    Tenant = x.Key?.Tenant ?? string.Empty,
                    Systems = s.Keys
                        .Where(k => k.Tenant == x.Key?.Tenant)
                        .OrderBy(o => o.SystemName)
                        .Select(y => new
                        {
                            System = y,
                            Meta = ToSafeMetaArray(s[y])
                        })
                        .ToArray()
                })
                .ToArray();
        }

        // Unwrap params-wrapping then stringify Value so System.Text.Json never sees unserializable object types
        private static object[] ToSafeMetaArray(object[] meta)
        {
            var flat = UnpackMeta(meta);
            return flat.OfType<AppStatusInfo>()
                       .Select(m => new { m.Key, Value = m.Value?.ToString() })
                       .ToArray<object>();
        }

        // Meta is stored as object[]{ AppStatusInfo[] } due to params wrapping — unwrap to a flat array
        private static object[] UnpackMeta(object[] meta)
        {
            if (meta == null || meta.Length == 0) return Array.Empty<object>();
            if (meta.Length == 1 && meta[0] is IEnumerable nested && !(meta[0] is string))
                return nested.Cast<object>().ToArray();
            return meta;
        }

        public override Task OnDisconnectedAsync(Exception exception)
        {
            this.ReportExceptions(() =>
            {
                OnVerbose("{0} disconnected", Context.ConnectionId);
                _lifecycle.OnNext(new ClientLifecycleEvent { ClientId = Context.ConnectionId, Connected = false });

                if (_routes.Count < 1)
                    return;

                var key = _routes.Where(route => route.Value == Context.ConnectionId).Select((item, _) => item.Key);

                if (key.Any())
                    RemoveRegistration(key.First());
            });

            return base.OnDisconnectedAsync(exception);
        }

        public IDisposable SendCommand(string route, string command)
        {
            try
            {
                if (Context.User == null)
                {
                    OnWarning("Not logged in. Fix bypass!");
                    //return;
                }

                if (String.IsNullOrWhiteSpace(command))
                {
                    OnWarning("How am I supposed to execute an empty command buddy?");
                    return Disposable.Empty;
                }

                // Resolve the calling client's registered route — that's the
                // originator for cmd-tracking. ExecuteCommand → SendClientCommand
                // records (cmd.Id → from) on IAppCmdManager so the eventual
                // CommandResult reaches THIS connection. Phase 7n4 fix: the UI
                // `sendCommand` path used to bypass tracking entirely
                // ("NO ROUTE/SOURCE" diagnostic in arena log).
                string from = null;
                foreach (var kv in _routes)
                {
                    if (kv.Value == Context.ConnectionId) { from = kv.Key; break; }
                }
                // If the caller never RegisterAsService'd (browser default),
                // synthesise a connId-keyed route so the result still lands
                // on this connection. Routes containing '\' are treated as
                // remote by AppCommandService.ExecuteCommand; a connId-only
                // key (no backslash) keeps that branch unaffected.
                if (string.IsNullOrEmpty(from)) from = Context.ConnectionId;

                return _resolver.Resolve<IAppCommandService>().ExecuteCommand(route, command, from).Do(result =>
                {
                    OnInformation("{0}", result);
                })
                .Catch<object, Exception>(e =>
                {
                    OnWarning("x {0}", e.Message);
                    return new object().ToObservable();
                }).Until();
            }
            catch (ArgumentException e)
            {
                OnWarning(e.Message);
            }
            catch (Exception e)
            {
                OnError(e);
            }

            return Disposable.Empty;
        }

        public void RegisterAsService(string route) =>
            RegisterRoute(Context.ConnectionId, route);

        public void RemoveRegistration(string route) =>
            RemoveRoute(route);

        public void DisposeSubscription()
        {
            Groups.RemoveFromGroupAsync(Context.ConnectionId, Context.User.Identity.Name);
        }

        public void EventReceived(RemoteEventReceived evt)
        {
            this.ReportExceptions(() =>
            {
                _context.Clients.All.SendAsync("EventReceived", evt);
            });
        }
        public void LogReceived(RemoteEventReceived evt)
        {
            this.ReportExceptions(() =>
            {
                //dont send to services?! only send to listeners?
                _context.Clients.All.SendAsync("LogReceived", evt);
            });
        }

        public void PublishBatch(string[] events)
        {
            foreach (var e in events) Publish(e);
        }

        public void Publish(string @event) =>
            PublishFromClient(Context.ConnectionId, @event);

        // Add(IRxnQuestion) and FlushCommands(route) are now on IAppCmdManager
        // (RoutableAppCmdManager singleton). Phase 7n4 refactor: cmd routing
        // is channel-agnostic, owned by the manager. Hubs are pure transports
        // and only expose the SendToClientAsync surface for the manager to
        // dispatch through.

        // ── IEventHub explicit surface (transport-agnostic) ───────────────────
        // Implemented directly on this class so consumers can DI on IEventHub
        // and get either this (SignalR) or RedisEventHub at runtime. Calls into
        // the existing private state - no separate state container needed.

        public IReadOnlyDictionary<string, string> Routes =>
            _routes.ToDictionary(kv => kv.Key, kv => kv.Value);

        public void RegisterRoute(string clientId, string route)
        {
            var rootRoute = route.AsRootRoute();
            OnVerbose("Registering route for connectionId '{0}' --> '{1}'", clientId, rootRoute);
            _routes.AddOrReplace(rootRoute, clientId);
            // Drain any cmds that queued on the manager while this route was
            // unregistered. Phase 7n4 refactor: queue lives on
            // IAppCmdManager (RoutableAppCmdManager), not on the status store.
            foreach (var c in _appCmds.FlushCommands(route))
            {
                _context.Clients.Client(clientId).SendAsync("Subscribe", c.Serialise().ResolveAs(c.GetType()));
            }
        }

        public void RemoveRoute(string route)
        {
            OnVerbose("Removed live route '{0}', commands will now be queued for status signals", route);
            _routes.Remove(route.AsRootRoute());
        }

        public void PublishFromClient(string clientId, string serializedEvent)
        {
            var deserialized = serializedEvent.Deserialise(serializedEvent.GetTypeFromJson(_resolver)) as IRxn;
            _rxnManager.Publish(deserialized).Until(e => OnError(new Exception($"Failed to publish from remote client! ", e)));

            // Track originator on the shared cmd manager. Reverse-lookup the
            // publishing client's registered route — the manager keys by
            // route (stable across reconnect), not connId. Phase 7n4 refactor:
            // tracking lives on IAppCmdManager, not in this hub's private
            // dicts.
            if (deserialized is IUniqueRxn q && deserialized is not CommandResult)
            {
                string foundRoute = null;
                foreach (var kv in _routes)
                {
                    if (kv.Value == clientId) { foundRoute = kv.Key; break; }
                }
                if (!string.IsNullOrEmpty(foundRoute))
                {
                    _appCmds.TrackOriginator(q.Id, foundRoute);
                    OnInformation("EventsHub.PublishFromClient: tracked cmd Id={0} type={1} clientId={2} route={3}",
                        q.Id, q.GetType().Name, clientId, foundRoute);
                }
                else
                {
                    OnInformation("EventsHub.PublishFromClient: cmd Id={0} type={1} clientId={2} has NO registered route — result will not be routable back",
                        q.Id, q.GetType().Name, clientId);
                }
            }

            // Telemetry (AppHeartbeat, AppResourceInfo, SystemStatusEvent...) must NOT be
            // forwarded: each bridge re-publishes whatever it receives via Subscribe, so
            // forwarding telemetry creates an infinite echo loop that crashes the cluster.
            // CommandResults arriving here from a SignalR client (e.g. worker pushing back
            // a result) are routed via the local-bus subscription on RouteCommandResultToInitiator,
            // which uses _appCmds.ResolveAndForgetOriginator — same shared store.
        }

        public Task SendToClientAsync(string clientId, string method, object payload) =>
            _context.Clients.Client(clientId).SendAsync(method, payload);

        public Task BroadcastAsync(string method, object payload) =>
            _context.Clients.All.SendAsync(method, payload);

        public IObservable<ClientLifecycleEvent> ClientLifecycle => _lifecycle;

        // IEventHub.HasRoute — case-insensitive lookup (route casing has
        // historically drifted across callsites). Cheaper than copying
        // Routes.ToDictionary just to check membership.
        public bool HasRoute(string route)
        {
            if (string.IsNullOrEmpty(route)) return false;
            var rootRoute = route.AsRootRoute();
            return _routes.ContainsKey(rootRoute);
        }
    }
}
