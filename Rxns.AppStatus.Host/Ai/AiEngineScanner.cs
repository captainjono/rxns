using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Rxns.Logging;

namespace Rxns.AppStatus.Host.Ai
{
    /// <summary>
    /// Discovered host candidate from a CIDR sweep.
    /// </summary>
    public class AiEngineScanResult
    {
        public string Url { get; set; }
        public string Kind { get; set; }              // "ollama" | "foundry" — guessed from response shape
        public List<string> Models { get; set; }      // when /v1/models returns
        public int LatencyMs { get; set; }
    }

    /// <summary>
    /// Parallel CIDR sweep for OpenAI-compat AI servers (Ollama, Foundry Local).
    /// Probes <c>GET http://{ip}:{port}/v1/models</c> with a short timeout per
    /// host; classifies responders as Ollama vs Foundry by sniffing the response
    /// (Ollama also exposes <c>/api/tags</c>; Foundry doesn't).
    ///
    /// <para>Intended to be called from <see cref="AiChatController"/> behind an
    /// explicit user-confirmation step in the UI. Default per-host timeout is
    /// 1.5s; a /24 sweep on two ports therefore tops out around 3–4 seconds
    /// real-time when run with ~64 parallel probes.</para>
    /// </summary>
    public class AiEngineScanner
    {
        // Per-probe timeout. Bumped from 2s after observing legitimate Ollama
        // responses on warm-cache machines exceeding 2s under load.
        private const int ProbeTimeoutSeconds = 5;

        // Pre-init dual-stack-aware HttpClient. The default handler honours
        // both IPv4 and IPv6 listeners ("::" dual-stack bind, as Ollama uses
        // by default on Windows), so the probe doesn't have to choose.
        private static readonly HttpClient _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(ProbeTimeoutSeconds)
        };

        // Foundry Local picks a random port at service-start. The fixed
        // sentinel 5273 stays as a default but the real port can range up
        // into the 54000s; we'd need 'foundry service status' to discover
        // dynamically. The other common spot is 5272/5274 in Foundry docs.
        public static readonly int[] DefaultPorts = { 11434, 5273, 5272, 5274 };

        /// <summary>Sweep the given CIDR for AI servers. Yields one result per
        /// host that responded with a parseable model list.</summary>
        public async Task<List<AiEngineScanResult>> ScanAsync(string cidr, int[] ports = null, int parallelism = 64, CancellationToken ct = default)
        {
            ports = ports != null && ports.Length > 0 ? ports : DefaultPorts;
            var ips = ExpandCidr(cidr).ToList();
            if (ips.Count == 0) return new List<AiEngineScanResult>();

            // Hosts x ports — flat list of (ip, port) targets.
            var targets = ips.SelectMany(ip => ports.Select(p => (ip, p))).ToList();

            var results = new System.Collections.Concurrent.ConcurrentBag<AiEngineScanResult>();
            using var gate = new SemaphoreSlim(Math.Max(1, parallelism));
            var tasks = targets.Select(async t =>
            {
                await gate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    var r = await ProbeAsync(t.ip, t.p, ct).ConfigureAwait(false);
                    if (r != null) results.Add(r);
                }
                finally { gate.Release(); }
            }).ToArray();
            await Task.WhenAll(tasks).ConfigureAwait(false);

            return results
                .OrderBy(r => r.Url, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>Single-host probe — fast TCP-then-HTTP check. Returns null
        /// when the host doesn't respond with a recognisable AI surface.
        /// Logs every probe outcome to <c>LogDebug("AiEngineScanner")</c> so
        /// scan failures aren't silently swallowed.</summary>
        public async Task<AiEngineScanResult> ProbeAsync(string ip, int port, CancellationToken ct = default)
        {
            var url = "http://" + ip + ":" + port;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(ProbeTimeoutSeconds));
                using var req = new HttpRequestMessage(HttpMethod.Get, url + "/v1/models");
                using var resp = await _http.SendAsync(req, cts.Token).ConfigureAwait(false);
                sw.Stop();

                if (!resp.IsSuccessStatusCode)
                {
                    ("scan " + url + "/v1/models -> HTTP " + (int)resp.StatusCode + " in " + sw.ElapsedMilliseconds + "ms").LogDebug("AiEngineScanner");
                    return null;
                }

                var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                var models = ParseModels(text);
                var kind = GuessKind(port, text);
                ("scan " + url + "/v1/models -> OK kind=" + kind + " models=" + models.Count + " in " + sw.ElapsedMilliseconds + "ms").LogDebug("AiEngineScanner");
                return new AiEngineScanResult
                {
                    Url = url,
                    Kind = kind,
                    Models = models,
                    LatencyMs = (int)sw.ElapsedMilliseconds
                };
            }
            catch (TaskCanceledException)
            {
                ("scan " + url + "/v1/models -> timeout after " + sw.ElapsedMilliseconds + "ms").LogDebug("AiEngineScanner");
                return null;
            }
            catch (HttpRequestException hex)
            {
                ("scan " + url + "/v1/models -> connect failed: " + hex.Message + " (inner: " + (hex.InnerException?.Message ?? "none") + ")").LogDebug("AiEngineScanner");
                return null;
            }
            catch (Exception ex)
            {
                ("scan " + url + "/v1/models -> " + ex.GetType().Name + ": " + ex.Message).LogDebug("AiEngineScanner");
                return null;
            }
        }

        private static List<string> ParseModels(string responseJson)
        {
            try
            {
                var doc = JObject.Parse(responseJson);
                var arr = doc["data"] as JArray;
                if (arr == null) return new List<string>();
                return arr.OfType<JObject>()
                    .Select(o => (string)o["id"])
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();
            }
            catch
            {
                return new List<string>();
            }
        }

        // Crude classifier: port 11434 → Ollama, 5273 → Foundry. Falls back to
        // looking for Ollama-specific id formats (':' tag separator like
        // "qwen2.5-coder:7b") when port is non-standard.
        private static string GuessKind(int port, string responseJson)
        {
            if (port == 11434) return "ollama";
            if (port == 5273)  return "foundry";
            if (responseJson != null && responseJson.IndexOf(':') >= 0 && responseJson.IndexOf("\"id\"", StringComparison.OrdinalIgnoreCase) >= 0)
                return "ollama";
            return "foundry";
        }

        /// <summary>Expand an IPv4 CIDR (e.g. "192.168.1.0/24") into the list of
        /// host addresses. Caps at /16 (65k hosts) to prevent accidental
        /// massive sweeps. Throws <see cref="ArgumentException"/> on malformed
        /// input.</summary>
        public static IEnumerable<string> ExpandCidr(string cidr)
        {
            if (string.IsNullOrWhiteSpace(cidr)) throw new ArgumentException("cidr required");
            // Allow bare IP — treat as /32.
            if (!cidr.Contains('/')) cidr = cidr.Trim() + "/32";

            var parts = cidr.Split('/');
            if (parts.Length != 2) throw new ArgumentException("bad CIDR: " + cidr);
            if (!IPAddress.TryParse(parts[0], out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                throw new ArgumentException("bad CIDR address: " + parts[0]);
            if (!int.TryParse(parts[1], out var prefix) || prefix < 16 || prefix > 32)
                throw new ArgumentException("CIDR prefix must be /16..//32 (got /" + parts[1] + ")");

            uint addr = BitConverter.ToUInt32(ip.GetAddressBytes().Reverse().ToArray(), 0);
            // `mask` here is the HOST-bit mask (lower (32-prefix) bits set). For
            // /32 there are zero host bits, so mask=0 — earlier code had this
            // inverted (mask=0xFFFFFFFF) which made `network = addr & ~mask`
            // collapse to 0.0.0.0 for any /32 input. Caught by the scanner
            // behaviour tests in AiEngineScannerBehaviour.
            uint mask = prefix == 32 ? 0u : (uint)((1L << (32 - prefix)) - 1);
            uint network = addr & ~mask;
            uint broadcast = network | mask;

            // For /32 yield the single address; otherwise skip network + broadcast
            // (.0 and .255 in /24) to avoid wasted probes.
            uint start = prefix == 32 ? network : network + 1;
            uint end = prefix == 32 ? network : broadcast - 1;
            for (uint i = start; i <= end; i++)
            {
                var bytes = BitConverter.GetBytes(i).Reverse().ToArray();
                yield return new IPAddress(bytes).ToString();
            }
        }
    }
}
