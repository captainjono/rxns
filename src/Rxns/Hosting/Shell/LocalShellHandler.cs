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
    /// <see cref="PersistentShell"/> (one cmd.exe / bash per process, commands piped in via
    /// stdin). Each call shares shell state with the previous one so <c>cd</c>, env vars and
    /// aliases survive - the UX is "real terminal" rather than fresh-subshell-per-cmd.
    ///
    /// <para>While a command is running, output is streamed back as
    /// <see cref="RemoteShellPartialResult"/> events, rate-limited per
    /// <see cref="IRemoteShellStreamCfg"/> via <c>.Buffer</c> so a chatty cmd (50 KB <c>cat</c>,
    /// <c>dir /s</c>) collapses into a handful of partials instead of flooding SignalR with one
    /// frame per line - which previously caused mid-burst transport blips. The buffer is closed by
    /// the command's own stream ending, so the tail is delivered rather than raced for.</para>
    ///
    /// <para>The terminal <see cref="RemoteShellResult"/> carries exit code + cwd plus any fatal
    /// error; its Stdout/Stderr are empty so the UI doesn't double-render the last partial - except
    /// on a timeout, where Stderr is the only place the operator would otherwise learn that the
    /// command produced no result at all.</para>
    ///
    /// <para>Registered as a singleton IServiceCommandHandler on BOTH the hub and worker defs so
    /// the ServiceCommandExecutor can resolve a handler regardless of which role is the target. Not
    /// gated on cluster-worker availability - shell is diagnostic, not "work."</para>
    /// </summary>
    public class LocalShellHandler : IServiceCommandHandler<RemoteShellCmd>, IDisposable
    {
        /// <summary>
        /// How long a single command may take before the shell is written off.
        ///
        /// <para>Generous rather than tight: exceeding it is not "slow", it is "this shell will
        /// never answer again", and the recovery is to throw the process away. A legitimate slow
        /// command being mistaken for a wedge costs a rebuilt shell and a lost cwd.</para>
        /// </summary>
        public static TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);

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

        /// <summary>
        /// Yields a shell that is fit to run a command, building a new one when the current one is
        /// not.
        ///
        /// <para><see cref="PersistentShell.NeedsRebuild"/> covers both ways a shell stops being
        /// usable: the process died, or a command timed out and left it mid-parse. The second is
        /// the one that used to be missed - the shell was still alive, so it was handed back and
        /// swallowed every command after it.</para>
        /// </summary>
        private IObservable<PersistentShell> GetOrCreateShell()
        {
            return Observable.Defer(() =>
            {
                lock (_shellLock)
                {
                    if (_shell != null && _shell.NeedsRebuild)
                    {
                        "PersistentShell needs rebuilding (exited, or wedged by a command that never returned); disposing".LogDebug();
                        try { _shell.Dispose(); } catch { }
                        _shell = null;
                    }
                    if (_shell != null) return Observable.Return(_shell);
                }

                return PersistentShell.Create()
                    .Select(built =>
                    {
                        lock (_shellLock)
                        {
                            // Another command may have built one while this was starting. Keep the
                            // one already published so both callers share a shell, and discard the
                            // loser rather than leaking its process.
                            if (_shell == null || _shell.NeedsRebuild)
                            {
                                _shell = built;
                                "PersistentShell started for RemoteShellCmd handler".LogDebug();
                                return built;
                            }
                        }
                        try { built.Dispose(); } catch { }
                        return _shell;
                    });
            });
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
                RunOnce(command)
                    // Bounded retry: a shell that died between acquiring it and writing to it is a
                    // transient we can absorb, and rebuilding is what the next acquire does anyway.
                    // Exactly one retry, so a command that kills the shell cannot loop forever. A
                    // TIMEOUT never reaches here - it completes normally with a timed-out result,
                    // because re-running a command that may still be executing would double any
                    // side effect it has already had.
                    .Catch<CommandResult, Exception>(first =>
                    {
                        $"RemoteShell[{command.Id}] attempt 1/2 failed: {first.GetType().Name}: {first.Message}".LogDebug();
                        ResetShell();
                        return RunOnce(command)
                            .Catch<CommandResult, Exception>(second =>
                            {
                                $"RemoteShell[{command.Id}] attempt 2/2 failed: {second.GetType().Name}: {second.Message}".LogDebug();
                                ResetShell();
                                return Observable.Return(PublishAndSummarise(command, -1, null, second.ToString(), 0, 0, 0));
                            });
                    }));
        }

        private IObservable<CommandResult> RunOnce(RemoteShellCmd command)
        {
            var cmd = command.DecodedCmd;
            var workerName = _appInfo?.Name ?? Environment.MachineName;
            var sequence = 0;
            var totalOutBytes = 0;
            var totalErrBytes = 0;

            return GetOrCreateShell().SelectMany(shell => shell
                .Run(cmd, CommandTimeout)
                // One subscription to the command, two readers of it: the partial emitter and the
                // terminal result. Publish keeps them on the same sequence, so the buffer below is
                // completed by the command ending rather than by a timer the caller hopes is long
                // enough - which is what used to drop the last partial of a fast command.
                .Publish(frames =>
                {
                    var partials = frames
                        .Where(f => !f.IsResult)
                        .Select(f => f.Output)
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

                            // Producer-side truncation: keep each IRxn comfortably under the
                            // SignalR transport cap. If a buffer holds more, the next batch is
                            // emitted sooner - no line is lost, it is split across more partials.
                            var stdoutText = Cap(stdoutSb.ToString());
                            var stderrText = Cap(stderrSb.ToString());

                            // The bridge handles disconnect-buffering and requeue on failure, so
                            // this Publish never silently drops a partial - worst case it queues
                            // until reconnect.
                            _rxnManager.Publish(new RemoteShellPartialResult
                            {
                                Worker = workerName,
                                Stdout = stdoutText,
                                Stderr = stderrText,
                                Sequence = Interlocked.Increment(ref sequence),
                                InResponseTo = command.Id
                            }).Until();
                        })
                        .IgnoreElements()
                        .Select(_ => default(PersistentShell.Frame));

                    return partials.Merge(frames.Where(f => f.IsResult));
                })
                .LastAsync()
                .Select(result => PublishAndSummarise(
                    command,
                    result.ExitCode,
                    result.Cwd,
                    result.TimedOut
                        ? $"The command produced no result within {CommandTimeout.TotalSeconds:0}s. The shell was left mid-parse - an unbalanced quote does this - and has been discarded; the next command gets a new one, with cwd and environment reset."
                        : null,
                    sequence, totalOutBytes, totalErrBytes)));
        }

        private string Cap(string s)
        {
            const int MaxBytesPerStream = 16 * 1024;
            return s.Length > MaxBytesPerStream
                ? s.Substring(0, MaxBytesPerStream) + "\n...[partial truncated]"
                : s;
        }

        private CommandResult PublishAndSummarise(RemoteShellCmd command, int exitCode, string cwd,
            string errText, int sequence, int totalOutBytes, int totalErrBytes)
        {
            _rxnManager.Publish(new RemoteShellResult
            {
                Worker = _appInfo?.Name ?? Environment.MachineName,
                Cmd = command.DecodedCmd,
                Stdout = string.Empty,
                Stderr = errText ?? string.Empty,
                ExitCode = exitCode,
                Cwd = cwd,
                InResponseTo = command.Id
            }).Until();

            $"RemoteShell[{command.Id}] exit: {exitCode} ({totalOutBytes}B out / {totalErrBytes}B err in {sequence} partials)".LogDebug();

            return CommandResult
                .Success($"shell [{exitCode}] {totalOutBytes}B out / {totalErrBytes}B err / {sequence} partials")
                .AsResultOf(command);
        }

        public void Dispose()
        {
            ResetShell();
        }
    }
}
