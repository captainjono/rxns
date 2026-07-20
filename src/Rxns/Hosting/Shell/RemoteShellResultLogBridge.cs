using System;
using Rxns;
using Rxns.Hosting;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns.Hosting.Shell
{
    /// <summary>
    /// Arena-side bridge: subscribes to <see cref="RemoteShellPartialResult"/> +
    /// <see cref="RemoteShellResult"/> on the local bus and logs a parseable
    /// line per event so external pollers of /systemstatus/log can correlate
    /// shell output with their dispatched cmdId.
    ///
    /// <para>
    /// Why: <see cref="LocalShellHandler"/> publishes RemoteShell* events via
    /// the central RxnManager. Under SignalR the worker→arena RLM bridge
    /// also surfaces the worker's <c>LogDebug</c> calls in the arena's REST
    /// log endpoint, so <c>TestArenaMonitor.ps1 -RemoteShell</c> could regex
    /// the worker's <c>RemoteShell[id] exit: N</c> log line. Under lossless
    /// (Redis Streams) the worker's LogDebug doesn't bridge to the arena's
    /// local log store — script times out at "no response within 30s" even
    /// though the EventsHub broadcast pump delivered the result to the UI
    /// just fine. This bridge unconditionally logs the script-recognised
    /// pattern from arena-side consumption, decoupling the script's
    /// observation surface from the RLM transport.
    /// </para>
    ///
    /// <para>
    /// Pattern matched by <c>Invoke-RemoteShell</c> in TestArenaMonitor.ps1
    /// (the <c>arena.</c> prefix is intentional — it distinguishes these
    /// arena-side bridged log entries from the worker's
    /// <c>LocalShellHandler</c> direct LogDebug output, which the
    /// "exactly-one-result" regression test counts to verify cmds don't
    /// loop):
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
        public IObservable<IRxn> Process(RemoteShellPartialResult e)
        {
            var stdout = (e.Stdout ?? string.Empty).Replace("\r", string.Empty).Replace("\n", "\\n");
            var stderr = (e.Stderr ?? string.Empty).Replace("\r", string.Empty).Replace("\n", "\\n");
            if (stdout.Length > 0)
                $"arena.RemoteShell[{e.InResponseTo}] stdout: {stdout}".LogDebug();
            if (stderr.Length > 0)
                $"arena.RemoteShell[{e.InResponseTo}] stderr: {stderr}".LogDebug();
            return Rxn.Empty<IRxn>();
        }

        public IObservable<IRxn> Process(RemoteShellResult e)
        {
            $"arena.RemoteShell[{e.InResponseTo}] exit: {e.ExitCode}".LogDebug();
            return Rxn.Empty<IRxn>();
        }
    }
}
