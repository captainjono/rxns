using System;
using Rxns.DDD.Commanding;
using Rxns.Interfaces;

namespace Rxns.Hosting.Shell
{
    /// <summary>
    /// Cross-platform shell-on-worker command used by the TestArena Remote Shell UI.
    /// <para>
    /// Wire-format gotcha (<c>CLAUDE.md</c>): <c>ServiceCommand.Parse</c> splits on
    /// spaces, so <c>Cmd</c> is a single base64 token - multi-word shell commands
    /// would otherwise shift args and mismatch ctor arity. <see cref="DecodedCmd"/>
    /// base64-decodes at handler time.
    /// </para>
    /// <para>
    /// Returns <see cref="RemoteShellResult"/> via the event bus so the UI can
    /// filter updates for the corresponding <c>InResponseTo</c>.
    /// </para>
    /// </summary>
    public class RemoteShellCmd : ServiceCommand
    {
        public string CmdB64 { get; set; }

        public RemoteShellCmd() { }
        public RemoteShellCmd(string cmdB64) { CmdB64 = cmdB64; }

        public string DecodedCmd =>
            string.IsNullOrEmpty(CmdB64)
                ? string.Empty
                : System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(CmdB64));

        // Fluent factory: hides the base64 + RxnQuestion wrapping for callers
        // that want to publish a shell command via /events/publish.
        //
        //   RemoteShellCmd.FromCmd("echo hi").For("NoTenant\\W1")
        //     => RxnQuestion { Destination = "NoTenant\\W1",
        //                      Options     = "RemoteShellCmd <b64>" }
        //
        // Used by tests, TestArenaMonitor's RemoteShell flag, and the TestArena
        // browser's Remote Shell panel — all of which were duplicating the
        // base64-encode + RxnQuestion shape inline.
        public static RemoteShellCmdBuilder FromCmd(string shell) => new RemoteShellCmdBuilder(shell);

        public class RemoteShellCmdBuilder
        {
            private readonly string _shell;
            private string _id = Guid.NewGuid().ToString();
            internal RemoteShellCmdBuilder(string shell) { _shell = shell; }

            public RemoteShellCmdBuilder WithId(string id) { _id = id; return this; }

            public Rxns.DDD.Commanding.RxnQuestion For(string route)
            {
                var b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(_shell ?? string.Empty));
                return new Rxns.DDD.Commanding.RxnQuestion
                {
                    Destination = route,
                    Options     = $"RemoteShellCmd {b64}",
                    Id          = _id
                };
            }
        }
    }

    public class RemoteShellResult : IRxn
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public DateTime At { get; set; } = DateTime.Now;
        public string Worker { get; set; }
        public string Cmd { get; set; }
        public string Stdout { get; set; }
        public string Stderr { get; set; }
        public int ExitCode { get; set; }
        public string InResponseTo { get; set; }

        /// <summary>
        /// Working directory of the persistent shell after this command ran.
        /// Lets the Remote Shell UI / `thebfg cmd` REPL render a real prompt
        /// (e.g. <c>C:\jan\thebfg&gt;</c>) without having to track cwd on the
        /// client. Empty when the shell doesn't report cwd.
        /// </summary>
        public string Cwd { get; set; }
    }

    /// <summary>
    /// Streamed chunk of in-flight RemoteShellCmd output. The persistent shell
    /// flushes one of these every <c>IRemoteShellStreamCfg.BufferDuration</c>
    /// or every <c>MaxLinesPerPartial</c> / <c>MaxBytesPerPartial</c>
    /// (whichever first) so a chatty command produces incremental UI updates
    /// instead of a single 64 KB-truncated dump at the end. Match by
    /// <see cref="InResponseTo"/> = the originating <c>RemoteShellCmd.Id</c>.
    /// The terminal <see cref="RemoteShellResult"/> carries the exit code +
    /// cwd; partials never do.
    /// </summary>
    public class RemoteShellPartialResult : IRxn
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public DateTime At { get; set; } = DateTime.Now;
        public string Worker { get; set; }
        public string Stdout { get; set; }
        public string Stderr { get; set; }
        public int Sequence { get; set; }
        public string InResponseTo { get; set; }
    }
}
