using System;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading;
using Rxns;
using Rxns.DDD.Commanding;
using Rxns.Health;
using Rxns.Hosting;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns.Hosting.Shell
{
    /// <summary>
    /// Runs RemoteShellCmd in the current process via a persistent
    /// <see cref="PersistentShell"/> (one cmd.exe / bash per process,
    /// commands piped in via stdin). Each call shares shell state with
    /// the previous one so <c>cd</c>, env vars, and aliases survive —
    /// the UX is "real terminal" rather than fresh-subshell-per-cmd.
    ///
    /// While a command is running, output is streamed back as
    /// <see cref="RemoteShellPartialResult"/> events. The shell's
    /// <see cref="PersistentShell.Lines"/> observable is rate-limited
    /// per <see cref="IRemoteShellStreamCfg"/> via <c>.Buffer</c> so a
    /// chatty cmd (50 KB <c>cat</c>, <c>dir /s</c>) collapses into a
    /// handful of partials instead of flooding SignalR with one frame
    /// per line — which previously caused mid-burst transport blips.
    ///
    /// The terminal <see cref="RemoteShellResult"/> carries exit code +
    /// cwd plus any fatal error; its Stdout/Stderr are empty so the UI
    /// doesn't double-render the last partial.
    ///
    /// Registered as a singleton IServiceCommandHandler on BOTH the arena
    /// and worker defs so the ServiceCommandExecutor can resolve a handler
    /// regardless of which role is the target. Not gated on cluster-worker
    /// availability — shell is diagnostic, not "work."
    /// </summary>
    public class LocalShellHandler : IServiceCommandHandler<RemoteShellCmd>, IDisposable
    {
        private readonly IRxnManager<IRxn> _rxnManager;
        private readonly IRxnAppInfo _appInfo;
        private readonly IRemoteShellStreamCfg _streamCfg;
        private PersistentShell _shell;
        private readonly object _shellLock = new object();

        public LocalShellHandler(IRxnManager<IRxn> rxnManager, IRxnAppInfo appInfo, IRemoteShellStreamCfg streamCfg)
        {
            _rxnManager = rxnManager;
            _appInfo = appInfo;
            _streamCfg = streamCfg;
        }

        private PersistentShell GetOrCreateShell()
        {
            lock (_shellLock)
            {
                // Auto-rebuild on a dead shell. Without this guard the next
                // RemoteShellCmd throws "Persistent shell has exited" forever
                // — operator's UI silently broken until the worker restarts.
                if (_shell != null && !_shell.IsAlive)
                {
                    "PersistentShell process had exited; disposing and rebuilding".LogDebug();
                    try { _shell.Dispose(); } catch { }
                    _shell = null;
                }
                if (_shell == null)
                {
                    _shell = new PersistentShell();
                    "PersistentShell started for RemoteShellCmd handler".LogDebug();
                }
                return _shell;
            }
        }

        private void ResetShell()
        {
            lock (_shellLock)
            {
                try { _shell?.Dispose(); } catch { }
                _shell = null;
            }
        }

        public IObservable<CommandResult> Handle(RemoteShellCmd command)
        {
            return Rxn.DfrCreate<CommandResult>(() =>
            {
                var cmd = command.DecodedCmd;
                var workerName = _appInfo?.Name ?? Environment.MachineName;
                int sequence = 0;
                int totalOutBytes = 0, totalErrBytes = 0;
                int exitCode = -1;
                string cwd = null;
                string fatalErr = null;

                // Bounded retry: shell death rebuilds on next GetOrCreate.
                // We retry the cmd ONCE so a transient death doesn't lose it,
                // never more so a poisonous cmd can't loop forever.
                for (int attempt = 1; attempt <= 2; attempt++)
                {
                    IDisposable lineSub = null;
                    try
                    {
                        var shell = GetOrCreateShell();

                        // Subscribe to the shell's line stream for the
                        // duration of THIS cmd (PersistentShell._gate
                        // guarantees no overlap with another cmd's
                        // subscription). Buffer collapses bursts:
                        //   - close every BufferDuration window, OR
                        //   - close every MaxLinesPerPartial lines.
                        // The byte-cap from cfg is enforced at emit time.
                        // Net effect: 50 KB / 1000-line `cat` produces ~5
                        // partials at default cfg instead of 1000 SignalR
                        // messages, which is what was causing transport
                        // bounces under load.
                        lineSub = shell.Lines
                            .Buffer(_streamCfg.BufferDuration, _streamCfg.MaxLinesPerPartial)
                            .Where(b => b.Count > 0)
                            .Do(batch =>
                            {
                                var stdoutSb = new StringBuilder();
                                var stderrSb = new StringBuilder();
                                foreach (var line in batch)
                                {
                                    if (line.Kind == PersistentShell.StreamKind.Stderr)
                                    {
                                        stderrSb.AppendLine(line.Text);
                                        Interlocked.Add(ref totalErrBytes, line.Text.Length + 1);
                                    }
                                    else
                                    {
                                        stdoutSb.AppendLine(line.Text);
                                        Interlocked.Add(ref totalOutBytes, line.Text.Length + 1);
                                    }
                                }
                                // Producer-side truncation: keep each IRxn comfortably
                                // under the SignalR transport cap (24 KB per side). If
                                // the buffer has more, the bridge's BufferUntilSize
                                // emits the next batch sooner — so we never lose a
                                // line, just split across more partials.
                                const int MaxBytesPerStream = 16 * 1024;
                                var stdoutText = stdoutSb.ToString();
                                var stderrText = stderrSb.ToString();
                                if (stdoutText.Length > MaxBytesPerStream)
                                    stdoutText = stdoutText.Substring(0, MaxBytesPerStream) + "\n...[partial truncated]";
                                if (stderrText.Length > MaxBytesPerStream)
                                    stderrText = stderrText.Substring(0, MaxBytesPerStream) + "\n...[partial truncated]";
                                var partial = new RemoteShellPartialResult
                                {
                                    Worker       = workerName,
                                    Stdout       = stdoutText,
                                    Stderr       = stderrText,
                                    Sequence     = Interlocked.Increment(ref sequence),
                                    InResponseTo = command.Id
                                };
                                // Bridge handles disconnect-buffering + requeue
                                // on InvokeAsync failure (Rxn.MakeReliable inside
                                // SignalRRxnManagerBridge), so this Publish never
                                // silently drops a partial — worst case the bridge
                                // queues until reconnect.
                                _rxnManager.Publish(partial).Until();
                            })
                            .Until(err => $"RemoteShell[{command.Id}] line stream error: {err.Message}".LogDebug());

                        var task = shell.RunAsync(cmd, TimeSpan.FromSeconds(30));
                        task.Wait();
                        (exitCode, cwd) = task.Result;

                        // After the shell hits its markers, give Buffer a
                        // last window to flush the tail before tearing
                        // down the subscription. Without this delay the
                        // last partial of a fast cmd gets dropped because
                        // we dispose lineSub before the window closes.
                        Thread.Sleep((int)_streamCfg.BufferDuration.TotalMilliseconds + 50);

                        fatalErr = null;
                        break;
                    }
                    catch (Exception ex)
                    {
                        var inner = (ex as AggregateException)?.InnerException ?? ex;
                        $"RemoteShell[{command.Id}] attempt {attempt}/2 failed: {inner.GetType().Name}: {inner.Message}".LogDebug();
                        fatalErr = inner.ToString();
                        ResetShell();
                        if (attempt == 2) break;
                    }
                    finally
                    {
                        try { lineSub?.Dispose(); } catch { }
                    }
                }

                var result = new RemoteShellResult
                {
                    Worker       = workerName,
                    Cmd          = cmd,
                    Stdout       = string.Empty,
                    Stderr       = fatalErr ?? string.Empty,
                    ExitCode     = exitCode,
                    Cwd          = cwd,
                    InResponseTo = command.Id
                };
                _rxnManager.Publish(result).Until();

                $"RemoteShell[{command.Id}] exit: {exitCode} ({totalOutBytes}B out / {totalErrBytes}B err in {sequence} partials)".LogDebug();

                var summary = $"shell [{exitCode}] {totalOutBytes}B out / {totalErrBytes}B err / {sequence} partials";
                return Observable.Return(CommandResult.Success(summary).AsResultOf(command));
            });
        }

        public void Dispose()
        {
            ResetShell();
        }
    }
}
