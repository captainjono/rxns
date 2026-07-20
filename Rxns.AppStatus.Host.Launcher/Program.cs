using System;
using System.Threading.Tasks;

namespace Rxns.AppStatus.Host.Launcher
{
    /// <summary>
    /// Trivial entry-point exe wrapping <see cref="AppStatusPortal.StartAsync"/>.
    /// Used by integration tests (and ops) to boot the portal as a subprocess
    /// without compiling a custom hosting app each time.
    ///
    /// Usage:
    ///   Rxns.AppStatus.Host.Launcher --port 5060
    ///   Rxns.AppStatus.Host.Launcher --port 5060 --system myapp
    ///   Rxns.AppStatus.Host.Launcher --port 5060 --dist C:/.../Web/dist
    ///
    /// On startup it prints a single line:
    ///   [host] ready on http://*:PORT
    /// so callers can wait for that signal rather than blind-poll.
    /// </summary>
    public static class Program
    {
        public static async Task<int> Main(string[] args)
        {
            var port = ArgInt(args, "--port", 5060);
            var system = ArgString(args, "--system", "rxns-host");
            var dist = ArgString(args, "--dist", AppStatusPortal.ResolveHtml5Root());

            var cfg = new AppStatusHostCfg
            {
                Port = port,
                Html5Root = dist,
                SystemName = system
            };

            Console.WriteLine("[host] starting on http://*:" + port + "  (dist=" + (dist ?? "<none>") + ", system=" + system + ")");
            var task = AppStatusPortal.StartAsync(cfg);

            // Print the ready signal AFTER returning from StartAsync's bind; the task
            // resolves once Kestrel is listening. Caller polls the API to confirm.
            Console.WriteLine("[host] ready on http://*:" + port);

            try { await task; }
            catch (Exception ex) { Console.Error.WriteLine("[host] aborted: " + ex.Message); return 1; }
            return 0;
        }

        private static string ArgString(string[] args, string name, string fallback)
        {
            for (var i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return fallback;
        }

        private static int ArgInt(string[] args, string name, int fallback)
        {
            var s = ArgString(args, name, null);
            return int.TryParse(s, out var v) ? v : fallback;
        }
    }
}
