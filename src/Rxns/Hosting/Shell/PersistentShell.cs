using System;
using System.Diagnostics;
using System.IO;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Rxns;
using Rxns.Logging;

namespace Rxns.Hosting.Shell
{
    /// <summary>
    /// One long-lived <c>cmd.exe</c> / <c>bash</c> per process. Each
    /// <see cref="RemoteShellCmd"/> is fed in via stdin; output is read
    /// line-by-line up until a unique marker so successive commands share
    /// the same shell — <c>cd</c>, env vars, aliases, and pipelines all
    /// keep their state across calls.
    ///
    /// Line streams are exposed as <see cref="Lines"/> (an <c>IObservable</c>),
    /// so callers compose <c>.Buffer/.Throttle/.Sample</c> on top to control
    /// downstream pressure (e.g. SignalR partial-result emit rate). Marker
    /// lines are filtered out internally — only "real" cmd output reaches the
    /// observable.
    ///
    /// Process lifecycle is managed by <c>Rxn.Create</c>: kill-on-Dispose,
    /// line-by-line stdout/stderr drain, and stdin redirection (via
    /// <c>redirectStdIn:true</c>) are all built in. The shell sub is held in
    /// <c>_shellSub</c> and disposed in <see cref="Dispose"/> to tear down
    /// the underlying process cleanly.
    ///
    /// Why a process-wide singleton instead of per-route or per-session:
    /// <see cref="LocalShellHandler"/> is a per-process singleton, the worker
    /// process serves one operator's interactive session at a time in
    /// practice, and adding session-id plumbing through ServiceCommand wire
    /// format isn't worth the breakage. The internal <c>_gate</c> semaphore
    /// serialises <see cref="RunAsync"/> calls so concurrent dispatches share
    /// the shell sequentially.
    /// </summary>
    public sealed class PersistentShell : IDisposable
    {
        public enum StreamKind { Stdout, Stderr }

        public readonly struct Line
        {
            public readonly string Text;
            public readonly StreamKind Kind;
            public Line(string text, StreamKind kind) { Text = text; Kind = kind; }
        }

        private Process _proc;
        private StreamWriter _stdin;
        private readonly bool _isWindows;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private readonly Subject<Line> _lines = new Subject<Line>();
        private IDisposable _shellSub;
        private TaskCompletionSource<int> _stdoutDone;
        private TaskCompletionSource<bool> _stderrDone;
        private string _outMarker;
        private string _errMarker;
        private string _lastCwd;
        private int _disposed;

        /// <summary>
        /// Hot stream of every non-marker line read from cmd.exe / bash, in
        /// arrival order. Subscribe per cmd, dispose when the cmd ends — the
        /// gate semaphore guarantees no two cmds are ever subscribing at once.
        /// </summary>
        public IObservable<Line> Lines => _lines;

        public bool IsAlive => _proc != null && !_proc.HasExited;

        public PersistentShell()
        {
            _isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            var exe = _isWindows ? "cmd.exe" : "/bin/bash";
            // /Q on cmd suppresses input echo. /V:ON enables delayed expansion
            // globally so framed cmds can use !ERRORLEVEL! without setlocal
            // (cmd.exe's 32-deep setlocal stack would overflow after enough
            // RemoteShellCmds otherwise).
            // bash --noediting keeps line discipline minimal; without -i bash
            // also avoids reading rc files and printing prompts.
            var args = _isWindows ? "/Q /K /V:ON" : "--noediting";

            // Process lifecycle via Rxn.Create — onInfo/onError fire per stdout/
            // stderr line; redirectStdIn=true wires p.StandardInput so we can
            // pipe successive cmds. Subscribing immediately starts the proc;
            // OnNext yields the running Process which we capture for stdin.
            var ready = new ManualResetEventSlim();
            Exception startupError = null;
            _shellSub = Rxn.Create(
                pathToProcess: exe,
                args: args,
                onInfo: line => HandleLine(line, StreamKind.Stdout),
                onError: line => HandleLine(line, StreamKind.Stderr),
                asChild: true,
                env: null,
                noWindow: true,
                redirectStdIn: true)
            .Subscribe(disposable =>
            {
                // Rxn.Create.OnNext gives us the Process (typed as IDisposable
                // in the public signature). We need the concrete type for
                // StandardInput + ExitCode + HasExited.
                if (disposable is Process p)
                {
                    _proc = p;
                    _stdin = p.StandardInput;
                    p.Exited += (_, __) =>
                    {
                        try
                        {
                            $"PersistentShell process exited unexpectedly. ExitCode={SafeExitCode(p)}. The next RemoteShellCmd will trigger a clean rebuild.".LogDebug();
                        }
                        catch { }
                    };
                    ready.Set();
                }
            }, err =>
            {
                startupError = err;
                ready.Set();
            });

            if (!ready.Wait(TimeSpan.FromSeconds(5)) || _proc == null)
            {
                try { _shellSub?.Dispose(); } catch { }
                throw new InvalidOperationException(
                    "Failed to start persistent shell" +
                    (startupError != null ? $": {startupError.Message}" : ""));
            }

            // Disable cmd.exe's prompt echo on each command. Bash has no
            // prompt without -i so nothing to do there.
            if (_isWindows)
            {
                try
                {
                    _stdin.WriteLine("@prompt $S$S");
                    _stdin.WriteLine("@echo off");
                    _stdin.Flush();
                }
                catch (Exception ex) { $"PersistentShell init write failed: {ex.Message}".LogDebug(); }
            }
        }

        private static string SafeExitCode(Process p)
        {
            try { return p.ExitCode.ToString(); } catch { return "?"; }
        }

        private void HandleLine(string line, StreamKind kind)
        {
            if (line == null) return;
            var marker = kind == StreamKind.Stderr ? _errMarker : _outMarker;
            // IndexOf, not StartsWith — cmd.exe emits its prompt (`@prompt $S$S`
            // = "  ") at the end of the previous cmd's last line, so when the
            // user cmd produces NO stdout (e.g. a silent `cd /d "C:\jan"`),
            // the marker line gets concatenated as `  __BFG_OUT_xxx__:cwd`.
            // StartsWith returned false → marker never matched → _stdoutDone
            // never set → RunAsync timed out. Fixed by parsing the marker from
            // wherever it appears in the line. Marker tokens are random GUIDs
            // (__BFG_OUT_<32hex>__) so the false-positive risk of a user cmd
            // producing the marker substring in their own output is negligible.
            // Phase 7n4 follow-up: caught by remoteshellcmd_returns_cwd_via_marker_after_cd.
            int idx = -1;
            if (marker != null) idx = line.IndexOf(marker, StringComparison.Ordinal);
            if (idx >= 0)
            {
                if (kind == StreamKind.Stderr)
                {
                    _stderrDone?.TrySetResult(true);
                }
                else
                {
                    // Marker shape: "MARKER:cwd" — keep parsing trivial; if
                    // anything past the colon, treat as cwd. Defensive against
                    // a future `for /f` ever returning empty (Windows always
                    // returns SOMETHING for `cd`).
                    var rest = line.Substring(idx + marker.Length);
                    if (rest.StartsWith(":") && rest.Length > 1)
                        _lastCwd = rest.Substring(1).TrimEnd();
                    _stdoutDone?.TrySetResult(0);
                }
                return;
            }
            // Real cmd output → push onto the observable. Wrapped in a try
            // because subscribers can throw and we don't want one bad subscriber
            // to take down the drain task.
            try { _lines.OnNext(new Line(line, kind)); }
            catch (Exception ex) { $"PersistentShell.Lines.OnNext threw: {ex.Message}".LogDebug(); }
        }

        /// <summary>
        /// Writes <paramref name="cmd"/> to the shell's stdin, then platform-
        /// specific marker lines that signal end-of-command on stdout (with
        /// the exit code + cwd suffixed) and stderr. Line output flows out
        /// via <see cref="Lines"/> as it arrives — caller subscribes there
        /// for the duration of this call. Returns when both markers are seen
        /// or <paramref name="timeout"/> elapses.
        /// </summary>
        public async Task<(int exitCode, string cwd)> RunAsync(string cmd, TimeSpan timeout)
        {
            await _gate.WaitAsync();
            try
            {
                if (_proc.HasExited)
                    throw new InvalidOperationException("Persistent shell has exited");

                var token = Guid.NewGuid().ToString("N");
                _outMarker = $"__BFG_OUT_{token}__";
                _errMarker = $"__BFG_ERR_{token}__";
                _stdoutDone = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                _stderrDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _lastCwd = null;

                // Marker carries cwd for the UI prompt, NOT exit code (the
                // exit-code path is what dragged in /V:ON `!ERRORLEVEL!`
                // delayed expansion which leaked literal `!BFG_EX!` to
                // stdout when the env wasn't right). cwd uses cmd.exe's
                // for-variable substitution (%P), which is a parse-time
                // for-loop construct that doesn't need delayed expansion
                // and never leaks.
                //
                // Multi-line framing: each line is a fresh cmd.exe parse
                // pass. `for /f "delims=" %P in ('cd') do echo M:%P`
                // captures the cwd from a `cd` subprocess and emits the
                // marker with it appended. Linux uses `pwd` substitution.
                //
                // We deliberately do NOT wrap the user cmd in `( ... )`.
                // The original parens-wrap was needed only for SINGLE-LINE
                // `&`-chained framing (where `for /l ... do <body>` would
                // consume the chained marker emission as part of the body).
                // With multi-line framing each line is parsed separately,
                // so the parens are unnecessary AND introduce cmd.exe's
                // quirky parens semantics around `cd`/`cd /d` (which can
                // hang the marker drain on `(cd /d "C:\")` due to the
                // `\"` escape inside parens).
                string framed;
                if (_isWindows)
                {
                    framed = $"{cmd}\r\n"
                           + $"for /f \"delims=\" %P in ('cd') do echo {_outMarker}:%P\r\n"
                           + $"echo {_errMarker} 1>&2";
                }
                else
                {
                    framed = $"{cmd}\n"
                           + $"printf '%s:%s\\n' '{_outMarker}' \"$(pwd)\"\n"
                           + $"printf '%s\\n' '{_errMarker}' >&2";
                }

                try
                {
                    await _stdin.WriteLineAsync(framed);
                    await _stdin.FlushAsync();
                }
                catch (Exception ex)
                {
                    $"PersistentShell stdin write failed: {ex.GetType().Name}: {ex.Message} (HasExited={_proc.HasExited}, ExitCode={SafeExitCode(_proc)})".LogDebug();
                    throw;
                }

                using (var cts = new CancellationTokenSource(timeout))
                {
                    var bothDone = Task.WhenAll(_stdoutDone.Task, _stderrDone.Task);
                    var winner = await Task.WhenAny(bothDone, Task.Delay(timeout, cts.Token));

                    if (winner != bothDone)
                        return (-1, _lastCwd);

                    int exit = await _stdoutDone.Task;
                    return (exit, _lastCwd);
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            try { _stdin?.WriteLine("exit"); _stdin?.Flush(); } catch { }
            try { _stdin?.Dispose(); } catch { }
            // Disposing the Rxn.Create subscription kills the underlying process
            // (asChild:true → p.Kill()). No need to do that ourselves.
            try { _shellSub?.Dispose(); } catch { }
            try { _lines.OnCompleted(); } catch { }
            try { _lines.Dispose(); } catch { }
            try { _gate.Dispose(); } catch { }
        }
    }
}
