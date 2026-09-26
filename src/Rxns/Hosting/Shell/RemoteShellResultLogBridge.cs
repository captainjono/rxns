using System;
using Rxns;
using Rxns.Hosting;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns.Hosting.Shell
{
    /// <summary>
    /// Hub-side bridge: subscribes to <see cref="RemoteShellPartialResult"/> +
    /// <see cref="RemoteShellResult"/> on the local bus and logs a parseable
    /// line per event so external pollers of /systemstatus/log can correlate
    /// shell output with their dispatched cmdId.
    ///
    /// <para>
    /// Why: <see cref="LocalShellHandler"/> publishes RemoteShell* events via
    /// the central RxnManager. Under SignalR the worker→hub RLM bridge
    /// also surfaces the worker's <c>LogDebug</c> calls in the hub's REST
    /// log endpoint, so <c>TestArenaMonitor.ps1 -RemoteShell</c> could regex
    /// the worker's <c>RemoteShell[id] exit: N</c> log line. Under lossless
    /// (Redis Streams) the worker's LogDebug doesn't bridge to the hub's
    /// local log store — script times out at "no response within 30s" even
    /// though the EventsHub broadcast pump delivered the result to the UI
    /// just fine. This bridge unconditionally logs the script-recognised
    /// pattern from hub-side consumption, decoupling the script's
    /// observation surface from the RLM transport.
    /// </para>
    ///
    /// <para>
    /// Pattern matched by <c>Invoke-RemoteShell</c> in TestArenaMonitor.ps1
    /// (see ArenaPrefix for why the prefix matters):
    /// <code>
    ///   arena.RemoteShell[&lt;cmdId&gt;] stdout: &lt;line\\nline&gt;
    ///   arena.RemoteShell[&lt;cmdId&gt;] stderr: &lt;line\\nline&gt;
    ///   arena.RemoteShell[&lt;cmdId&gt;] exit: &lt;code&gt;
    /// </code>
    /// Embedded newlines are collapsed to <c>\n</c> on emit so the entire
    /// payload fits one log entry; the script re-expands them on display.
    /// </para>
    /// </summary>
    public class RemoteShellResultLogBridge : ReportsStatus, IRxnProcessor<RemoteShellPartialResult>, IRxnProcessor<RemoteShellResult>
    {
        /// <summary>
        /// Marks a line as the arena's, not the worker's.
        ///
        /// <para>Load-bearing, not decoration. The worker's own LocalShellHandler writes a
        /// byte-identical line for the same command, so without this there is no way to tell the
        /// two apart. Two things then break at once: a caller waiting for the arena to confirm a
        /// result waits forever under lossless, because the line it is shown belongs to the
        /// worker and the arena's never arrives distinguishable; and the "exactly one result"
        /// regression counts both lines and reports a duplicate dispatch that never happened.</para>
        ///
        /// <para>Three spellings were in play and none agreed: the doc below said "hub.", the
        /// code emitted no prefix at all, and every consumer - TestArenaMonitor.ps1 and the
        /// tests - matched on "arena.". A constant so the emitter and anything compiled against
        /// it cannot drift apart again.</para>
        /// </summary>
        public const string ArenaPrefix = "arena.";


        public IObservable<IRxn> Process(RemoteShellPartialResult e)
        {
            var stdout = (e.Stdout ?? string.Empty).Replace("\r", string.Empty).Replace("\n", "\\n");
            var stderr = (e.Stderr ?? string.Empty).Replace("\r", string.Empty).Replace("\n", "\\n");
            if (stdout.Length > 0)
                $"{ArenaPrefix}RemoteShell[{e.InResponseTo}] stdout: {stdout}".LogDebug();
            if (stderr.Length > 0)
                $"{ArenaPrefix}RemoteShell[{e.InResponseTo}] stderr: {stderr}".LogDebug();
            return Rxn.Empty<IRxn>();
        }

        public IObservable<IRxn> Process(RemoteShellResult e)
        {
            $"{ArenaPrefix}RemoteShell[{e.InResponseTo}] exit: {e.ExitCode}".LogDebug();
            return Rxn.Empty<IRxn>();
        }
    }
}
