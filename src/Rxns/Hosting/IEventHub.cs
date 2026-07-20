using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Rxns.Hosting.Updates;
using Rxns.Interfaces;

namespace Rxns.Hosting
{
    /// <summary>
    /// Cluster event-hub abstraction. Owns ONLY the channel-mechanics surface:
    /// route map, per-client send path, lifecycle. Two implementations:
    ///
    ///   - SignalREventHub: backed by an ASP.NET Core SignalR Hub. Default.
    ///   - RedisEventHub:   backed by per-client Redis Streams + a route hash,
    ///                      heartbeat-watcher for lifecycle. Used in lossless
    ///                      mode to escape SignalR connection-storm at scale.
    ///
    /// <para>Cmd routing + originator tracking lives on
    /// <see cref="IAppCmdManager"/>, NOT here. Hubs <b>depend on</b>
    /// <see cref="IAppCmdManager"/>; they do not implement it. This separation
    /// fixes the phase 7n4 result-back race + UI <c>sendCommand</c>
    /// "NO ROUTE/SOURCE" path: cmd state is a single source of truth instead
    /// of fragmented across each transport's private dicts.</para>
    ///
    /// Consumers (bfgWorkerRemoteOrchestrator, AppCommandService, etc) take
    /// <see cref="IAppCmdManager"/> for cmd routing and only this interface
    /// for raw channel sends.
    /// </summary>
    public interface IEventHub
    {
        /// <summary>
        /// route -> clientId. Read-only view; mutation goes through
        /// RegisterRoute / RemoveRoute so the impl can keep its internal
        /// representation consistent (in-process dict for SignalR, Redis hash
        /// for Redis). Iteration is safe; entries reflect the currently-known
        /// connected clients at iteration time.
        /// </summary>
        IReadOnlyDictionary<string, string> Routes { get; }

        /// <summary>
        /// Bind a route to a specific clientId. Called when a client (worker /
        /// remote target) self-registers with its route, e.g. "notenant\W1_0".
        /// </summary>
        void RegisterRoute(string clientId, string route);

        /// <summary>
        /// Cheap membership check used by <see cref="IAppCmdManager.Add"/> to
        /// pick which channel owns a destination route. Equivalent to
        /// <c>Routes.ContainsKey(route)</c> but lets impls optimise (e.g.
        /// case-normalisation already applied).
        /// </summary>
        bool HasRoute(string route);

        /// <summary>
        /// Drop a route binding. Called explicitly by the client OR
        /// implicitly by the impl on lifecycle disconnect.
        /// </summary>
        void RemoveRoute(string route);

        /// <summary>
        /// Publish a serialized event from a remote client. Round-trips into
        /// the local IRxnManager. For IUniqueRxn questions (commands), tracks
        /// the originator so the eventual CommandResult can be routed back to
        /// just that client (the "routable backing channel" guarantee).
        /// </summary>
        void PublishFromClient(string clientId, string serializedEvent);

        /// <summary>
        /// Send a typed event to a single client by its id. Used for routed
        /// IRxnQuestion delivery (Add) and for CommandResult back-routing.
        /// </summary>
        Task SendToClientAsync(string clientId, string method, object payload);

        /// <summary>
        /// Broadcast a typed event to all currently-connected clients. Used
        /// for EventReceived / LogReceived UI fanout and StatusUpdates push.
        /// </summary>
        Task BroadcastAsync(string method, object payload);

        /// <summary>
        /// Lifecycle stream of client connect/disconnect events. SignalR impl
        /// drives this from Hub.OnConnectedAsync/OnDisconnectedAsync; Redis
        /// impl drives it from heartbeat-watcher state transitions (with a
        /// grace window for missed heartbeats).
        /// </summary>
        IObservable<ClientLifecycleEvent> ClientLifecycle { get; }
    }

    public class ClientLifecycleEvent
    {
        public string ClientId { get; set; }
        public bool Connected { get; set; }
    }
}
