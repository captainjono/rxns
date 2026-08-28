using System;
using System.Collections.Generic;
using System.IO;
using Rxns.Health.AppStatus;
using Rxns.Logging;

namespace Rxns.Hosting.Updates
{

    public interface IAppStatusCfg
    {
        bool ShouldAutoUnzipLogs { get; }
        string AppRoot { get; set; }
    }

    public class AppStatusCfg : IAppStatusCfg
    {
        public bool ShouldAutoUnzipLogs { get; set; }
        public string AppRoot { get; set; }
    }

    public interface IAppCmdManager
    {
        IEnumerable<IRxnQuestion> FlushCommands(string route);

        void Add(IRxnQuestion cmds);

        /// <summary>
        /// Whether this node can actually deliver <paramref name="cmds"/> to its destination - that
        /// is, whether some registered channel owns a matching route.
        ///
        /// <para>Callers need this because <see cref="Add"/> queues anything it cannot place, betting
        /// a future registration will claim it. That is right on a hub, which owns channels to every
        /// node and may simply be racing a reconnect. It is wrong anywhere else: a worker handed a
        /// command for a sibling has no channel to that sibling and never will, so the command sits
        /// pending forever while the sender counts it as delivered.</para>
        /// </summary>
        bool CanRoute(IRxnQuestion cmds);

        /// <summary>
        /// Records that <paramref name="commandId"/> originated from
        /// <paramref name="originatorRoute"/>. The eventual
        /// <c>CommandResult.InResponseTo</c> lookup via
        /// <see cref="ResolveAndForgetOriginator"/> returns this route, so any
        /// channel can route the result back to the originator regardless of
        /// which channel the result arrived on. Survives reconnects because
        /// resolution returns route (stable across reconnect), not the
        /// originator's connectionId (changes on reconnect).
        ///
        /// <para>Tracking lives on the cmd-manager (not on individual channels)
        /// so dispatches that enter via channel A and produce results that
        /// arrive via channel B still resolve correctly. This is the fix for
        /// the phase 7n4 result-back race + the UI <c>sendCommand</c>
        /// "NO ROUTE/SOURCE" path.</para>
        /// </summary>
        void TrackOriginator(string commandId, string originatorRoute);

        /// <summary>
        /// Returns and removes the originator route for
        /// <paramref name="commandId"/>, or null if not tracked. TryRemove
        /// semantics — idempotent across composite channels so the same
        /// result can't be double-delivered.
        /// </summary>
        string ResolveAndForgetOriginator(string commandId);

        /// <summary>
        /// Channels (<see cref="IEventHub"/> implementations) call this on
        /// construction so the cmd manager knows which transports are alive.
        /// <see cref="Add(IRxnQuestion)"/> iterates the registered channels
        /// to find one that owns the destination route, then dispatches via
        /// <see cref="IEventHub.SendToClientAsync"/>. Replaces the previous
        /// design where each channel had its own <c>Add</c> + cmd-tracking
        /// dicts (which fragmented routing across SignalR + Redis arenas).
        /// </summary>
        void RegisterChannel(Rxns.Hosting.IEventHub channel);
    }


    public interface IAppStatusStore
    {
        // Phase 7n4 refactor: this used to inherit IAppCmdManager. Cmd
        // routing + queueing moved to RoutableAppCmdManager so the store
        // is single-responsibility (log/cache/system-status only) and
        // hubs depend on IAppCmdManager (don't implement it).
        IDictionary<object, object> Cache { get; }

        IDictionary<string, Dictionary<SystemStatusEvent, object[]>> GetSystemStatus();

        /// <summary>
        /// Resets  the entire systemstatus cache
        /// </summary>
        void Clear();

        /// <summary>
        /// resets a particular systemstatus cache
        /// </summary>
        /// <param name="route">The route to clear</param>
        void ClearSystemStatus(string route);

        //this is SystemLogMeta but it doesnt appear part of this interface lib so its object
        //could be a generic i know, this is a work in progress though so im not fussed
        IEnumerable<object> GetLog();
        IObservable<string> SaveLog(string tenant, Stream log, string file);
        IObservable<AppLogInfo[]> ListLogs(string tenantId, int top = 3);
        IObservable<Stream> GetLogs(string tenantId, string file);
        void Add(LogMessage<Exception> message);
        void Add(LogMessage<string> message);
    }
}
