using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
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
    /// One long-lived <c>cmd.exe</c> / <c>bash</c> per process, driven reactively. Each command is
    /// fed in via stdin and framed by unique end-markers, so successive commands share the same
    /// shell - <c>cd</c>, env vars, aliases and pipelines all keep their state across calls.
    ///
    /// <para><see cref="Run"/> returns the command as a stream: its output arrives as
    /// <see cref="Frame"/>s and the stream completes with a terminal result frame. The window is
    /// the marker, not a flag set alongside one - which is what makes the tail reliable. The
    /// previous design signalled completion through a <c>TaskCompletionSource</c> while output went
    /// to a separate hot subject, so the caller had no way to know when the last line had been
    /// delivered and slept for a buffer window hoping it had. Completion and output now travel in
    /// one ordered sequence, so the last line cannot be dropped and cannot be attributed to the
    /// next command.</para>
    ///
    /// <para>Every raw line is stamped with the token of the command that was running when it was
    /// read, and each command only accepts its own. Without that, output that arrives late - after
    /// a command has timed out - is delivered to whichever command is running when it lands.</para>
    ///
    /// <para>Why a process-wide singleton rather than per-route or per-session:
    /// <see cref="LocalShellHandler"/> is a per-process singleton, the worker process serves one
    /// operator's interactive session at a time in practice, and adding session-id plumbing through
    /// the ServiceCommand wire format isn't worth the breakage. Commands are serialised onto the
    /// one shell by a gate, so concurrent dispatches queue rather than interleave.</para>
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

        /// <summary>
        /// One item in a command's stream: either a line of its output, or the terminal result.
        ///
        /// <para>A single sequence rather than two, so "this is the last line" and "here is the
        /// exit code" cannot be observed out of order or independently lost.</para>
        /// </summary>
        public readonly struct Frame
        {
            public readonly bool IsResult;
            public readonly Line Output;
            public readonly int ExitCode;
            public readonly string Cwd;
            public readonly bool TimedOut;

            private Frame(bool isResult, Line output, int exitCode, string cwd, bool timedOut)
            {
                IsResult = isResult; Output = output; ExitCode = exitCode; Cwd = cwd; TimedOut = timedOut;
            }

            public static Frame FromOutput(Line l) => new Frame(false, l, 0, null, false);
            public static Frame Result(int exitCode, string cwd) => new Frame(true, default, exitCode, cwd, false);
            public static Frame TimedOutAt(string cwd) => new Frame(true, default, -1, cwd, true);
        }

        /// <summary>A line as read from the process, tagged with the command that owned the shell at the time.</summary>
        private readonly struct Raw
        {
            public readonly Line Line;
            public readonly string Token;      // which command was running when this was read
            public readonly bool IsMarker;
            public readonly string Cwd;        // only on the stdout marker

            public Raw(Line line, string token, bool isMarker, string cwd)
            {
                Line = line; Token = token; IsMarker = isMarker; Cwd = cwd;
            }
        }

        private readonly Subject<Raw> _raw = new Subject<Raw>();
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private readonly bool _isWindows;

        private Process _proc;
        private StreamWriter _stdin;
        private IDisposable _shellSub;
        private string _outMarker;
        private string _errMarker;
        private string _currentToken;
        private int _disposed;
        private int _poisoned;

        public bool IsAlive => _proc != null && !_proc.HasExited;

        /// <summary>
        /// True once this shell can no longer be trusted to run another command.
        ///
        /// <para>Set when a command times out. A timeout does not mean "the command was slow" - it
        /// means the end-markers never came back, and the only way that happens is that the shell
        /// is still mid-parse and swallowed them as continuation of the user's command. An
        /// unbalanced quote does it: everything after is consumed looking for the closing one,
        /// including the two marker lines this class appends. The shell stays alive and keeps
        /// accepting input, so nothing looks broken - but every command after it is swallowed the
        /// same way, and the caller waits out the full timeout on each.</para>
        ///
        /// <para>Found on a live device: one malformed command left the worker answering nothing
        /// but <c>exit -1</c> for the rest of its life, with heartbeats still green. A parse state
        /// cannot be cleared from the outside, so the only honest recovery is a new process - see
        /// <see cref="LocalShellHandler"/>, which rebuilds on this flag.</para>
        /// </summary>
        public bool NeedsRebuild => !IsAlive || Volatile.Read(ref _poisoned) == 1;

        private PersistentShell()
        {
            _isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        }

        /// <summary>
        /// Starts the shell process and yields it once its stdin is wired.
        ///
        /// <para>Observable rather than a constructor because starting it is asynchronous: the
        /// process has to come up before there is anything to write to. Expressing that as a
        /// sequence lets the caller compose a timeout rather than block a thread on a wait handle
        /// and then re-derive whether it actually started.</para>
        /// </summary>
        public static IObservable<PersistentShell> Create(TimeSpan? startupTimeout = null)
        {
            return Observable.Create<PersistentShell>(o =>
            {
                var shell = new PersistentShell();
                var exe = shell._isWindows ? "cmd.exe" : "/bin/bash";
                // /Q on cmd suppresses input echo. /V:ON enables delayed expansion globally so
                // framed cmds can use !ERRORLEVEL! without setlocal (cmd.exe's 32-deep setlocal
                // stack would overflow after enough RemoteShellCmds otherwise).
                // bash --noediting keeps line discipline minimal; without -i bash also avoids
                // reading rc files and printing prompts.
                var args = shell._isWindows ? "/Q /K /V:ON" : "--noediting";

                // AsyncSubject, not Subject: Rxn.Create can hand back the process synchronously
                // inside Subscribe below, so the shell is often ready before there is anything
                // listening for it. A plain Subject drops that value on the floor and the caller
                // waits out the startup timeout for a shell that started fine. AsyncSubject holds
                // its single value and delivers it however late the subscription arrives.
                var started = new AsyncSubject<PersistentShell>();

                shell._shellSub = Rxn.Create(
                        pathToProcess: exe,
                        args: args,
                        onInfo: line => shell.HandleLine(line, StreamKind.Stdout),
                        onError: line => shell.HandleLine(line, StreamKind.Stderr),
                        asChild: true,
                        env: null,
                        noWindow: true,
                        redirectStdIn: true)
                    .Subscribe(disposable =>
                    {
                        // Rxn.Create's OnNext hands back the Process typed as IDisposable. The
                        // concrete type is what carries StandardInput / HasExited / ExitCode.
                        if (!(disposable is Process p)) return;

                        shell._proc = p;
                        shell._stdin = p.StandardInput;
                        p.Exited += (_, __) =>
                        {
                            try
                            {
                                $"PersistentShell process exited unexpectedly. ExitCode={SafeExitCode(p)}. It will be rebuilt before the next command.".LogDebug();
                            }
                            catch { }
                        };

                        // Settle the shell before anyone is allowed to use it, and prove it rather
                        // than assume it.
                        //
                        // Two things have to be true first: cmd.exe's per-command prompt is
                        // silenced, so marker parsing never has to contend with it; and the
                        // process is actually parsing stdin. The second is not free - a command
                        // framed and written while the shell is still coming up gets mangled, and
                        // comes back as a syntax error on stderr with no output. That is rare
                        // enough to look like a flake and costly enough to matter, because the
                        // moment it happens is right after a rebuild: the shell has just been
                        // replaced because a command wedged it, and the command sent to prove the
                        // replacement works is the one that gets eaten.
                        //
                        // So the shell echoes a token and is not handed out until it comes back.
                        // No sleep, no guess - the process itself says when it is listening.
                        var ready = "__BFG_READY_" + Guid.NewGuid().ToString("N") + "__";
                        var sawReady = shell._raw
                            .Where(r => r.Line.Text != null &&
                                        r.Line.Text.IndexOf(ready, StringComparison.Ordinal) >= 0)
                            .Take(1)
                            .Subscribe(_ =>
                            {
                                started.OnNext(shell);
                                started.OnCompleted();
                            });

                        try
                        {
                            if (shell._isWindows)
                            {
                                shell._stdin.WriteLine("@prompt $S$S");
                                shell._stdin.WriteLine("@echo off");
                            }
                            shell._stdin.WriteLine("echo " + ready);
                            shell._stdin.Flush();
                        }
                        catch (Exception ex)
                        {
                            $"PersistentShell init write failed: {ex.Message}".LogDebug();
                            try { sawReady.Dispose(); } catch { }
                            started.OnError(ex);
                        }
                    }, started.OnError);

                var subscription = started
                    .Timeout(startupTimeout ?? TimeSpan.FromSeconds(5))
                    .Catch<PersistentShell, Exception>(e =>
                    {
                        try { shell.Dispose(); } catch { }
                        return Observable.Throw<PersistentShell>(
                            new InvalidOperationException($"Failed to start persistent shell: {e.Message}", e));
                    })
                    .Subscribe(o);

                return subscription;
            });
        }

        private static string SafeExitCode(Process p)
        {
            try { return p.ExitCode.ToString(); } catch { return "?"; }
        }

        /// <summary>Keeps the offending command in the log without pasting a whole script into it.</summary>
        private static string Truncate(string cmd)
        {
            if (string.IsNullOrEmpty(cmd)) return string.Empty;
            var oneLine = cmd.Replace("\r", " ").Replace("\n", " ");
            return oneLine.Length <= 120 ? oneLine : oneLine.Substring(0, 120) + "...";
        }

        private void HandleLine(string line, StreamKind kind)
        {
            if (line == null) return;

            var token = Volatile.Read(ref _currentToken);
            var marker = kind == StreamKind.Stderr ? Volatile.Read(ref _errMarker) : Volatile.Read(ref _outMarker);

            // IndexOf, not StartsWith - cmd.exe emits its prompt (`@prompt $S$S` = "  ") at the end
            // of the previous cmd's last line, so when the user cmd produces NO stdout (e.g. a
            // silent `cd /d "C:\jan"`), the marker line arrives concatenated as
            // `  __BFG_OUT_xxx__:cwd`. StartsWith returned false, the marker never matched, and the
            // command hung until its timeout. Marker tokens are random GUIDs so a user command
            // producing the substring itself is not a practical concern.
            var idx = marker == null ? -1 : line.IndexOf(marker, StringComparison.Ordinal);
            if (idx >= 0)
            {
                string cwd = null;
                if (kind == StreamKind.Stdout)
                {
                    // Marker shape: "MARKER:cwd".
                    var rest = line.Substring(idx + marker.Length);
                    if (rest.StartsWith(":") && rest.Length > 1) cwd = rest.Substring(1).TrimEnd();
                }
                Publish(new Raw(new Line(line, kind), token, true, cwd));
                return;
            }

            Publish(new Raw(new Line(line, kind), token, false, null));
        }

        private void Publish(Raw raw)
        {
            // A disposed shell's process keeps draining for a moment after its subject is gone, so
            // the last few lines arrive with nowhere to go. That is ordinary shutdown, not a fault
            // worth a log line per line.
            if (Volatile.Read(ref _disposed) != 0) return;

            // A subscriber that throws must not take down the process's output drain.
            try { _raw.OnNext(raw); }
            catch (ObjectDisposedException) { }
            catch (Exception ex) { $"PersistentShell line pump threw: {ex.Message}".LogDebug(); }
        }

        /// <summary>
        /// Runs <paramref name="cmd"/> and returns its output followed by a terminal result frame.
        ///
        /// <para>Commands are serialised: the returned sequence waits for any command already in
        /// flight before it writes anything. The sequence completes when both end-markers have been
        /// seen, or - if they never arrive within <paramref name="timeout"/> - with a result frame
        /// whose <see cref="Frame.TimedOut"/> is set, after marking the shell for rebuild.</para>
        /// </summary>
        public IObservable<Frame> Run(string cmd, TimeSpan timeout)
        {
            return Observable.FromAsync(ct => _gate.WaitAsync(ct))
                .SelectMany(_ => RunExclusively(cmd, timeout)
                    .Finally(() =>
                    {
                        // Stop accepting this command's lines the moment it ends, so anything the
                        // shell emits afterwards is discarded rather than handed to the next one.
                        Volatile.Write(ref _currentToken, null);
                        Volatile.Write(ref _outMarker, null);
                        Volatile.Write(ref _errMarker, null);
                        _gate.Release();
                    }));
        }

        private IObservable<Frame> RunExclusively(string cmd, TimeSpan timeout)
        {
            return Observable.Defer(() =>
            {
                if (!IsAlive)
                    return Observable.Throw<Frame>(new InvalidOperationException("Persistent shell has exited"));

                var token = Guid.NewGuid().ToString("N");
                var outMarker = $"__BFG_OUT_{token}__";
                var errMarker = $"__BFG_ERR_{token}__";

                Volatile.Write(ref _outMarker, outMarker);
                Volatile.Write(ref _errMarker, errMarker);
                Volatile.Write(ref _currentToken, token);

                var markersSeen = 0;
                string cwd = null;

                // The command's window. Only lines stamped with this command's token are eligible,
                // so output left over from a command that has already ended cannot appear here.
                // TakeWhile closes the window on the second marker without emitting it, which is
                // what makes the tail deterministic: the sequence ends exactly where the shell says
                // the command ended, not a buffer-duration later.
                var body = _raw
                    .Where(r => r.Token == token)
                    .Do(r =>
                    {
                        if (!r.IsMarker) return;
                        Interlocked.Increment(ref markersSeen);
                        if (r.Cwd != null) cwd = r.Cwd;
                    })
                    .TakeWhile(_ => Volatile.Read(ref markersSeen) < 2)
                    .Where(r => !r.IsMarker)
                    .Select(r => Frame.FromOutput(r.Line));

                var withResult = body.Concat(Observable.Defer(() =>
                    Observable.Return(Frame.Result(0, cwd))));

                var writeThenListen = Observable.Create<Frame>(o =>
                {
                    // Subscribe before writing: the shell can answer faster than the write returns,
                    // and a reply that lands before there is a subscriber is simply lost.
                    var sub = withResult.Subscribe(o);
                    try
                    {
                        _stdin.WriteLine(FrameCommand(cmd, outMarker, errMarker));
                        _stdin.Flush();
                    }
                    catch (Exception ex)
                    {
                        $"PersistentShell stdin write failed: {ex.GetType().Name}: {ex.Message} (HasExited={_proc.HasExited}, ExitCode={SafeExitCode(_proc)})".LogDebug();
                        sub.Dispose();
                        o.OnError(ex);
                        return Disposable.Empty;
                    }
                    return sub;
                });

                // An absolute deadline, not an inter-line one: the budget is for the whole command,
                // which is what the caller is promised.
                return writeThenListen
                    .Timeout(DateTimeOffset.UtcNow + timeout)
                    .Catch<Frame, TimeoutException>(_ =>
                    {
                        // Not a slow command - a command whose end-markers never arrived, which
                        // means the shell consumed them as part of its own unfinished parse. It
                        // would do the same to every command after this one, so retire it rather
                        // than hand it back looking healthy.
                        Interlocked.Exchange(ref _poisoned, 1);
                        $"PersistentShell timed out after {timeout.TotalSeconds:0}s waiting for the end-markers of '{Truncate(cmd)}'. The shell is mid-parse (an unbalanced quote does this) and cannot be recovered; it will be rebuilt before the next command.".LogDebug();
                        return Observable.Return(Frame.TimedOutAt(cwd));
                    });
            });
        }

        /// <summary>
        /// Wraps the user's command with the marker lines that tell us where it ended.
        ///
        /// <para>The marker carries the cwd for the caller's prompt, NOT the exit code. Carrying the
        /// exit code is what dragged in <c>/V:ON</c> delayed expansion, which leaked a literal
        /// <c>!BFG_EX!</c> to stdout whenever the environment wasn't right. cwd uses cmd.exe's
        /// for-variable substitution (<c>%P</c>), a parse-time construct that needs no delayed
        /// expansion and never leaks.</para>
        ///
        /// <para>Multi-line framing: each line is a fresh parse pass. The user command is
        /// deliberately NOT wrapped in <c>( ... )</c> - the parens were only ever needed for
        /// single-line <c>&amp;</c>-chained framing, and they bring cmd.exe's quirky parens
        /// semantics around <c>cd</c> / <c>cd /d</c>, which can hang the marker drain on
        /// <c>(cd /d "C:\")</c> because of the <c>\"</c> escape inside parens.</para>
        /// </summary>
        private string FrameCommand(string cmd, string outMarker, string errMarker)
        {
            return _isWindows
                ? $"{cmd}\r\n"
                  + $"for /f \"delims=\" %P in ('cd') do echo {outMarker}:%P\r\n"
                  + $"echo {errMarker} 1>&2"
                : $"{cmd}\n"
                  + $"printf '%s:%s\\n' '{outMarker}' \"$(pwd)\"\n"
                  + $"printf '%s\\n' '{errMarker}' >&2";
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            try { _stdin?.WriteLine("exit"); _stdin?.Flush(); } catch { }
            try { _stdin?.Dispose(); } catch { }
            // Disposing the Rxn.Create subscription kills the underlying process (asChild:true).
            try { _shellSub?.Dispose(); } catch { }
            try { _raw.OnCompleted(); } catch { }
            try { _raw.Dispose(); } catch { }
            try { _gate.Dispose(); } catch { }
        }
    }
}
