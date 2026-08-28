using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Rxns.DDD.Commanding;   // for IRxnQuestion.IsFor extension
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns.Hosting.Updates
{
    /// <summary>
    /// Singleton <see cref="IAppCmdManager"/> impl. Owns ALL cmd-routing
    /// state: cmd→originator tracking, the pending-cmd queue (for routes
    /// that aren't yet registered on any channel), and the channel list.
    ///
    /// <para>
    /// Architectural rule (post phase 7n4): channels (<see cref="IEventHub"/>
    /// implementations) <b>depend on</b> <see cref="IAppCmdManager"/>; they
    /// do not implement it. <see cref="IAppStatusStore"/> is now a peer
    /// interface (log/cache/system-status only) — it no longer inherits
    /// <see cref="IAppCmdManager"/>, so cmd-routing state isn't fragmented
    /// between hubs and the status store.
    /// </para>
    ///
    /// <para>This replaces the previous design where each channel had its
    /// own <c>_commandSources</c>/<c>_commandRoutes</c> dict + the status
    /// store had its own pending-cmd queue. Three bugs that the refactor
    /// fixes:</para>
    /// <list type="bullet">
    ///   <item>7n4 round-2 hang — orchestrator publishes via SignalR, worker
    ///   emits result via Redis, RedisEventHub's empty dict drops it.</item>
    ///   <item>UI <c>sendCommand</c> "NO ROUTE/SOURCE" — UI cmds went via
    ///   <c>AppCommandService.SendClientCommand</c> which bypassed the Hub's
    ///   tracking entirely.</item>
    ///   <item>Mid-test reconnect drops — a connId stored in
    ///   <c>_commandSources</c> went stale after SignalR auto-reconnect.</item>
    /// </list>
    /// </summary>
    public sealed class RoutableAppCmdManager : IAppCmdManager
    {
        // Channels register at construction; the manager iterates them in
        // Add() to find one that owns the destination route. List rather
        // than dict so order is deterministic (registration order = lookup
        // order, matching the previous CompositeEventHub.Add precedence).
        private readonly List<IEventHub> _channels = new List<IEventHub>();
        private readonly object _channelsLock = new object();

        // commandId → originator route. Route is stable across reconnects
        // (worker name, e.g. "notenant\\w1_0") — connId would be wrong
        // because reconnect produces a fresh one.
        private readonly ConcurrentDictionary<string, string> _originators =
            new ConcurrentDictionary<string, string>();

        // Pending cmds for routes that aren't yet registered on any channel.
        // When a worker eventually registers via heartbeat (or a UI client
        // RegisterAsService's a route), the channel itself drains the queue
        // via FlushCommands(route) on its first SendToClientAsync to that
        // route. Lock-per-route via ConcurrentQueue's atomic semantics.
        private readonly ConcurrentDictionary<string, ConcurrentQueue<IRxnQuestion>> _pending =
            new ConcurrentDictionary<string, ConcurrentQueue<IRxnQuestion>>();

        public void RegisterChannel(IEventHub channel)
        {
            if (channel == null) return;
            lock (_channelsLock)
            {
                if (!_channels.Contains(channel))
                {
                    _channels.Add(channel);
                    $"RoutableAppCmdManager: registered channel {channel.GetType().Name} (total={_channels.Count})".LogDebug();
                }
            }
        }

        public void TrackOriginator(string commandId, string originatorRoute)
        {
            if (string.IsNullOrEmpty(commandId) || string.IsNullOrEmpty(originatorRoute)) return;
            _originators[commandId] = originatorRoute;
        }

        public string ResolveAndForgetOriginator(string commandId)
        {
            if (string.IsNullOrEmpty(commandId)) return null;
            return _originators.TryRemove(commandId, out var route) ? route : null;
        }

        public IEnumerable<IRxnQuestion> FlushCommands(string route)
        {
            if (string.IsNullOrEmpty(route)) return System.Linq.Enumerable.Empty<IRxnQuestion>();
            if (!_pending.TryRemove(route, out var queue)) return System.Linq.Enumerable.Empty<IRxnQuestion>();

            var drained = new List<IRxnQuestion>();
            while (queue.TryDequeue(out var cmd)) drained.Add(cmd);
            return drained;
        }

        /// <summary>
        /// Channels holding the destination exactly, before those that match it only loosely. Without
        /// this the first loose match wins and a command addressed to one node goes to another.
        /// </summary>
        private static IEnumerable<IEventHub> Ordered(IEventHub[] channels, IRxnQuestion cmds)
        {
            var destination = (cmds.Destination ?? string.Empty).AsRoute();

            // Exact route first, then a durable transport over an in-memory one.
            //
            // The durable preference matters more than it looks. A hub send to a connection id that
            // has since dropped succeeds silently - there is no exception to observe and no ack - so
            // the command is gone and the dispatcher counts it delivered. A stream consumer group
            // keyed by route holds the message until that route actually reads it. Measured: with the
            // hub preferred, one of two dispatches vanished in three runs out of four.
            return channels
                .OrderByDescending(ch => ch.Routes.Keys.Any(k => (k ?? string.Empty).AsRoute() == destination) ? 1 : 0)
                .ThenByDescending(ch => IsDurable(ch) ? 1 : 0);
        }

        /// <summary>
        /// Whether a channel redelivers rather than dropping when the far end is not listening right
        /// now. Matched by name to avoid a reference from this assembly to the Redis one.
        /// </summary>
        private static bool IsDurable(IEventHub channel) =>
            channel != null && channel.GetType().Name.IndexOf("Redis", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>
        /// Re-attempts delivery on every channel except the one that just failed, queuing if none
        /// of them owns the route - the same fate the command would have had if the failed channel
        /// had never claimed it.
        /// </summary>
        private void RetryElsewhere(IRxnQuestion cmds, string failedChannelType)
        {
            IEventHub[] snapshot;
            lock (_channelsLock) snapshot = _channels.Where(c => c.GetType().Name != failedChannelType).ToArray();

            foreach (var ch in snapshot)
                foreach (var kv in ch.Routes)
                    if (cmds.IsFor(kv.Key))
                    {
                        $"RoutableAppCmdManager: re-routing '{cmds.Destination}' via {ch.GetType().Name}".LogDebug();
                        _ = ch.SendToClientAsync(kv.Value, "Subscribe", cmds.Serialise().ResolveAs(cmds.GetType()));
                        return;
                    }

            _pending.GetOrAdd(cmds.Destination ?? string.Empty, _ => new ConcurrentQueue<IRxnQuestion>()).Enqueue(cmds);
        }

        public bool CanRoute(IRxnQuestion cmds)
        {
            if (cmds == null) return false;

            IEventHub[] snapshot;
            lock (_channelsLock) snapshot = _channels.ToArray();

            foreach (var ch in snapshot)
                foreach (var kv in ch.Routes)
                    if (cmds.IsFor(kv.Key))
                        return true;

            return false;
        }

        public void Add(IRxnQuestion cmds)
        {
            if (cmds == null) return;

            IEventHub[] snapshot;
            lock (_channelsLock) snapshot = _channels.ToArray();

            // First channel that owns a matching route wins. Registration
            // order = priority (SignalR registers first in current DI →
            // in-process delivery preferred over Redis stream).
            // Most specific route wins, then registration order. IsFor is a substring test, so a
            // channel registered under a broader key - a tenant, or any route that is a prefix -
            // also matches a command addressed to one particular node. Taking the first such match
            // handed another worker's work to whichever channel registered earliest, and the real
            // addressee never saw it: on a two-VM cluster, one VM ran everything and the other idled.
            foreach (var ch in Ordered(snapshot, cmds))
            {
                foreach (var kv in ch.Routes.OrderByDescending(r => (r.Key ?? string.Empty).Length))
                {
                    if (!cmds.IsFor(kv.Key)) continue;

                    // The success path was silent while the failure path logged, so a command that
                    // went to the wrong channel looked identical to one that went to the right one.
                    $"RoutableAppCmdManager.Add: {cmds.GetType().Name} dest='{cmds.Destination}' -> {ch.GetType().Name} route='{kv.Key}'".LogDebug();

                    var payload = cmds.Serialise().ResolveAs(cmds.GetType());

                    // Observe the send. It used to be fire-and-forget, so a channel that owned the
                    // route but could no longer reach the client - a dropped SignalR connection whose
                    // route mapping outlived it - swallowed the command and reported nothing. The
                    // work then never ran and the dispatcher counted it as delivered. On failure,
                    // fall back through the remaining channels rather than giving up on the first.
                    var attempt = ch.SendToClientAsync(kv.Value, "Subscribe", payload);
                    if (attempt != null)
                    {
                        var failedOn = ch.GetType().Name;
                        var destination = cmds.Destination;
                        attempt.ContinueWith(sent =>
                        {
                            $"RoutableAppCmdManager.Add: {failedOn} could not deliver to '{destination}' ({sent.Exception?.GetBaseException().Message}) - retrying on other channels".LogDebug();
                            RetryElsewhere(cmds, failedOn);
                        }, TaskContinuationOptions.OnlyOnFaulted);
                    }

                    // Drain any prior queued cmds for this route now that we
                    // have a live channel for it. Mirrors the per-hub Add()
                    // flush behaviour from before the refactor.
                    foreach (var queued in FlushCommands(kv.Key))
                        _ = ch.SendToClientAsync(kv.Value, "Subscribe",
                            queued.Serialise().ResolveAs(queued.GetType()));

                    return;
                }
            }

            // No channel owned the route — queue for later. A future
            // RegisterRoute or heartbeat-driven registration will pick
            // these up via FlushCommands when Add reruns for that route.
            var routeKey = cmds.Destination ?? string.Empty;
            var q = _pending.GetOrAdd(routeKey, _ => new ConcurrentQueue<IRxnQuestion>());
            q.Enqueue(cmds);
            $"RoutableAppCmdManager.Add: no channel owns route '{routeKey}' — queued (pending={q.Count})".LogDebug();
        }
    }
}
