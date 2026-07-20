using System.Threading;
using System.Threading.Tasks;

namespace Rxns.AppStatus.Host.Ai.Discovery
{
    /// <summary>
    /// Tiny abstraction over "run an external command, return its stdout +
    /// exit code". Exists so discovery adapters that shell out to vendor CLIs
    /// (<c>foundry service status</c>, <c>ollama list</c>, …) can be tested
    /// against a fake runner that returns canned output — no real CLI needed
    /// in the test rig.
    /// </summary>
    public interface ICommandRunner
    {
        Task<CommandResult> RunAsync(string executable, string arguments, int timeoutMs = 5000, CancellationToken ct = default);
    }

    public class CommandResult
    {
        public int ExitCode { get; set; }
        public string Stdout { get; set; }
        public string Stderr { get; set; }
        public bool TimedOut { get; set; }

        /// <summary>True when the executable could not be located on PATH —
        /// distinct from a non-zero exit code on a real run.</summary>
        public bool ExecutableMissing { get; set; }
    }
}
