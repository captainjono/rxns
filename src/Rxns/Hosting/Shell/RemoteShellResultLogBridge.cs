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


        /// <summary>
        /// Keeps one command's output from evicting everything else in the status ring.
        ///
        /// <para>The ring is a bounded, shared buffer - the same one every other reporter writes
        /// to. A single <c>dir /s</c> streams megabytes, and unbounded that pushes out the arena
        /// events someone is usually reading the log to find. Truncating marks what was cut so a
        /// short answer is never mistaken for a complete one; the full stream is on the hub, which
        /// is where an operator watching output interactively already is.</para>
        /// </summary>
        private const int MaxLoggedChars = 4000;

        private static string ForLog(string s)
        {
            var oneLine = (s ?? string.Empty).Replace("\r", string.Empty).Replace("\n", "\\n");
            return oneLine.Length <= MaxLoggedChars
                ? oneLine
                : oneLine.Substring(0, MaxLoggedChars) + $"...[truncated, {oneLine.Length} chars]";
        }

        public IObservable<IRxn> Process(RemoteShellPartialResult e)
        {
            // Reported to ReportStatus.Log, not with LogDebug and not on this instance's own
            // channel. LogDebug is the static console sink - it reaches the arena's stdout, and so
            // a file on the VM, but never /systemstatus/log, which is the endpoint this class
            // exists to feed. Reporting on `this` looks right and is just as dead: ReportsStatus
            // drops a message outright when its channel has no observers, and nothing subscribes
            // to this type's - only reporters the container wires up are aggregated. Proved on a
            // local arena: the bridge was constructed and subscribed, the worker shipped its
            // result, and the log still never mentioned the command.
            //
            // ReportStatus.Log is the app-wide reporter ("AppLog") that the status log demonstrably
            // carries, so a line put there is a line the poller can find. With this in place a
            // remote shell round-trips on a plain SignalR arena - no Redis, nothing lossless - in
            // about two and a half seconds, which is what `thebfg arena shell` had never managed.
            var stdout = ForLog(e.Stdout);
            var stderr = ForLog(e.Stderr);
            if (stdout.Length > 0)
                ReportStatus.Log.OnVerbose($"{ArenaPrefix}RemoteShell[{e.InResponseTo}] stdout: {stdout}");
            if (stderr.Length > 0)
                ReportStatus.Log.OnVerbose($"{ArenaPrefix}RemoteShell[{e.InResponseTo}] stderr: {stderr}");
            return Rxn.Empty<IRxn>();
        }

        public IObservable<IRxn> Process(RemoteShellResult e)
        {
            // Information rather than Verbose: this is the line that tells a caller the command is
            // over, so it has to survive whatever verbosity the arena is running at. Its stderr
            // carries the reason when a command produced no result at all.
            var stderr = ForLog(e.Stderr);
            if (stderr.Length > 0)
                ReportStatus.Log.OnInformation($"{ArenaPrefix}RemoteShell[{e.InResponseTo}] stderr: {stderr}");
            ReportStatus.Log.OnInformation($"{ArenaPrefix}RemoteShell[{e.InResponseTo}] exit: {e.ExitCode}");
            return Rxn.Empty<IRxn>();
        }
    }
}
