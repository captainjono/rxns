using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Rxns.AppStatus.Host.Ai.Discovery
{
    /// <summary>
    /// Default <see cref="ICommandRunner"/> — spawns a real OS process and
    /// captures stdout + stderr. Used in production; tests substitute a fake.
    /// </summary>
    public class ProcessCommandRunner : ICommandRunner
    {
        public async Task<CommandResult> RunAsync(string executable, string arguments, int timeoutMs = 5000, CancellationToken ct = default)
        {
            var psi = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments ?? string.Empty,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            Process p;
            try { p = Process.Start(psi); }
            catch (System.ComponentModel.Win32Exception)
            {
                // "The system cannot find the file specified." → exe not on PATH.
                return new CommandResult { ExecutableMissing = true, ExitCode = -1, Stdout = "", Stderr = "" };
            }
            catch (Exception ex)
            {
                return new CommandResult { ExitCode = -1, Stdout = "", Stderr = ex.Message };
            }

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(timeoutMs);
                var stdoutTask = p.StandardOutput.ReadToEndAsync();
                var stderrTask = p.StandardError.ReadToEndAsync();
                var exitedTask = p.WaitForExitAsync(cts.Token);

                try { await exitedTask.ConfigureAwait(false); }
                catch (OperationCanceledException)
                {
                    try { p.Kill(entireProcessTree: true); } catch { }
                    return new CommandResult
                    {
                        TimedOut = true,
                        ExitCode = -1,
                        Stdout = await SafeAwait(stdoutTask),
                        Stderr = await SafeAwait(stderrTask)
                    };
                }

                return new CommandResult
                {
                    ExitCode = p.ExitCode,
                    Stdout = await SafeAwait(stdoutTask),
                    Stderr = await SafeAwait(stderrTask)
                };
            }
            finally { try { p.Dispose(); } catch { } }
        }

        private static async Task<string> SafeAwait(Task<string> t)
        {
            try { return (await t.ConfigureAwait(false)) ?? string.Empty; }
            catch { return string.Empty; }
        }
    }
}
