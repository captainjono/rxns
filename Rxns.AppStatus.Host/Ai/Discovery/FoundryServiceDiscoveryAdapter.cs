using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Rxns.Logging;

namespace Rxns.AppStatus.Host.Ai.Discovery
{
    /// <summary>
    /// Discovers a Foundry Local instance by shelling out to
    /// <c>foundry service status</c> and parsing the printed endpoint URL.
    /// Foundry binds to a random port at service-start (observed: 54997, 5273
    /// is just one possibility) so a CIDR sweep on well-known ports misses it;
    /// the CLI is the source of truth for "where is it actually listening".
    ///
    /// <para>Sample successful stdout (Foundry 2025-era CLI, emoji prefix is
    /// real):</para>
    /// <code>🟢 Service is Started on http://127.0.0.1:54997/, PID 1968!</code>
    ///
    /// <para>If the foundry CLI isn't installed, the runner reports
    /// <see cref="CommandResult.ExecutableMissing"/> and we return an empty
    /// list — discovery is opportunistic.</para>
    /// </summary>
    public class FoundryServiceDiscoveryAdapter : IAiEngineDiscoveryAdapter
    {
        private readonly ICommandRunner _runner;
        private readonly AiEngineScanner _scanner;

        public FoundryServiceDiscoveryAdapter(ICommandRunner runner, AiEngineScanner scanner)
        {
            _runner = runner ?? new ProcessCommandRunner();
            _scanner = scanner ?? new AiEngineScanner();
        }

        public string Id    => "foundry-cli";
        public string Label => "Foundry Local (CLI)";

        // Matches the "Service is Started on http://host:port/" line and any
        // close variation foundry might print (extra punctuation tolerated).
        private static readonly Regex EndpointRegex =
            new Regex(@"(https?://[A-Za-z0-9\.\-]+:\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public async Task<IReadOnlyList<DiscoveredEngine>> DiscoverAsync(CancellationToken ct = default)
        {
            CommandResult cmd;
            try { cmd = await _runner.RunAsync("foundry", "service status", timeoutMs: 4000, ct: ct).ConfigureAwait(false); }
            catch (Exception ex)
            {
                ("FoundryServiceDiscoveryAdapter: runner threw: " + ex.Message).LogDebug("AiDiscovery");
                return new List<DiscoveredEngine>();
            }

            if (cmd == null || cmd.ExecutableMissing)
            {
                ("FoundryServiceDiscoveryAdapter: 'foundry' not on PATH — skipping").LogDebug("AiDiscovery");
                return new List<DiscoveredEngine>();
            }

            // Parse the URL out of stdout (it's where the service-up line lives).
            var url = ParseEndpoint(cmd.Stdout) ?? ParseEndpoint(cmd.Stderr);
            if (string.IsNullOrWhiteSpace(url))
            {
                ("FoundryServiceDiscoveryAdapter: no URL in 'foundry service status' output. exit=" + cmd.ExitCode +
                 " stdout=" + Truncate(cmd.Stdout, 120)).LogDebug("AiDiscovery");
                return new List<DiscoveredEngine>();
            }

            // Verify it actually serves the OpenAI surface — reuse the scanner's
            // /v1/models probe so we get the model list at the same time.
            var parsed = ParseHostPort(url);
            if (parsed == null) return new List<DiscoveredEngine>();

            var probe = await _scanner.ProbeAsync(parsed.Value.host, parsed.Value.port, ct).ConfigureAwait(false);
            var loadedModels = probe?.Models ?? new List<string>();

            // /v1/models on Foundry only returns LOADED models. A user who's
            // downloaded Phi-4-mini but hasn't `foundry model run`'d it would
            // otherwise never see Phi in the picker (the reported bug). Shell
            // `foundry model list` to enumerate installed-but-unloaded models
            // and merge them into the picker.
            var installedModels = await ListInstalledModelsAsync(ct).ConfigureAwait(false);

            var merged = MergeUnique(loadedModels, installedModels);

            return new List<DiscoveredEngine>
            {
                new DiscoveredEngine
                {
                    Url = url,
                    Kind = "foundry",
                    Models = merged,
                    LatencyMs = probe?.LatencyMs ?? 0
                }
            };
        }

        /// <summary>Shell <c>foundry cache ls</c> (the locally-downloaded
        /// catalogue — NOT <c>foundry model list</c>, which dumps every model
        /// in the remote catalogue and floods the picker) and parse out the
        /// installed model IDs. Returns an empty list on any failure —
        /// discovery is opportunistic.
        ///
        /// <para>Bug history: first cut used <c>foundry model list</c> which
        /// returned ~71 entries on a machine with only 2 downloaded — that's
        /// the catalogue, not the cache. <c>foundry cache ls</c> is the
        /// "what's actually on disk" command.</para></summary>
        public async Task<List<string>> ListInstalledModelsAsync(CancellationToken ct = default)
        {
            CommandResult cmd;
            try { cmd = await _runner.RunAsync("foundry", "cache ls", timeoutMs: 6000, ct: ct).ConfigureAwait(false); }
            catch (Exception ex)
            {
                ("FoundryServiceDiscoveryAdapter.ListInstalledModelsAsync: runner threw: " + ex.Message).LogDebug("AiDiscovery");
                return new List<string>();
            }
            if (cmd == null || cmd.ExecutableMissing || cmd.ExitCode != 0) return new List<string>();
            return ParseInstalledModels(cmd.Stdout);
        }

        /// <summary>Public for testability. Pulls model-id-shaped tokens from
        /// arbitrary <c>foundry model list</c> output. Filters out table
        /// headers, ANSI escapes, and rows that don't carry a recognisable id.</summary>
        public static List<string> ParseInstalledModels(string stdout)
        {
            var ids = new List<string>();
            if (string.IsNullOrWhiteSpace(stdout)) return ids;

            // Strip ANSI escapes so colourised CLI output doesn't break the regex.
            var clean = AnsiEscape.Replace(stdout, string.Empty);

            // Foundry IDs look like: phi-4-mini-instruct-qnn-npu:2,
            // qwen2.5-7b-instruct-qnn-npu:2, deepseek-r1-distill-7b-cpu:1, etc.
            // Match at least 4 chars + at least one dash so we don't false-positive
            // on column headers like "Model" or "Alias".
            foreach (Match m in ModelIdRegex.Matches(clean))
            {
                var id = m.Value.Trim();
                if (string.IsNullOrWhiteSpace(id)) continue;
                if (ids.IndexOf(id) >= 0) continue;
                // Defensive: reject anything that's clearly a column header.
                if (string.Equals(id, "model", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(id, "alias", StringComparison.OrdinalIgnoreCase)) continue;
                ids.Add(id);
            }
            return ids;
        }

        private static List<string> MergeUnique(List<string> a, List<string> b)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var merged = new List<string>();
            foreach (var src in new[] { a, b })
            {
                if (src == null) continue;
                foreach (var m in src)
                {
                    if (string.IsNullOrWhiteSpace(m)) continue;
                    if (seen.Add(m)) merged.Add(m);
                }
            }
            return merged;
        }

        private static readonly Regex AnsiEscape =
            new Regex(@"\x1b\[[0-9;]*[A-Za-z]", RegexOptions.Compiled);

        // Must START with a letter so we don't match IPv4 fragments like
        // "127.0.0.1:1" that show up in the same `foundry service status`
        // stdout we feed through here in the fallback path. Real Foundry
        // model IDs always start with a model-family name (phi-, qwen-,
        // deepseek-, llama-, mistral-, etc.).
        private static readonly Regex ModelIdRegex =
            new Regex(@"\b[a-z][a-z0-9]*(?:[-_.][a-z0-9]+){2,}(?::\d+)?\b",
                      RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>Public for testability — pulls the first <c>http(s)://host:port</c>
        /// substring out of arbitrary CLI output. Strips any trailing slash
        /// and punctuation.</summary>
        public static string ParseEndpoint(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var m = EndpointRegex.Match(text);
            return m.Success ? m.Groups[1].Value : null;
        }

        private static (string host, int port)? ParseHostPort(string url)
        {
            try
            {
                var u = new Uri(url);
                return (u.Host, u.Port);
            }
            catch { return null; }
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }
    }
}
