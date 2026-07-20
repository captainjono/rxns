using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Rxns.AppInsights;

namespace Rxns.AppStatus.Host.AppInsights
{
    /// <summary>
    /// Az CLI-backed AppInsights browser. Shells out to:
    ///   az monitor app-insights query --subscription &lt;sub&gt; -g &lt;rg&gt; -a &lt;app&gt; --analytics-query "&lt;KQL&gt;" --offset &lt;dur&gt; -o json
    ///
    /// The same wire <c>Augment-AppInsights.ps1</c> already uses — so the queries and
    /// permissions we have in that flow apply here unchanged.
    ///
    /// Why az CLI: zero extra .NET deps; whatever auth the operator has set up for
    /// `az login` is the auth the portal uses. Fail-soft when `az` isn't on PATH.
    /// </summary>
    public class AzCliAppInsightsBrowser : IAppInsightsBrowser
    {
        private readonly AppInsightsBrowserOptions _options;
        private static readonly Dictionary<string, string> _presets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["latency"]      = "requests | summarize p50=percentile(duration,50), p95=percentile(duration,95), p99=percentile(duration,99), count() by bin(timestamp, 5m), cloud_RoleName | order by timestamp desc",
            ["exceptions"]   = "exceptions | summarize count() by problemId, type, cloud_RoleName | order by count_ desc | take 50",
            ["topEndpoints"] = "requests | summarize count(), p95=percentile(duration,95), errorRate=avg(toint(success!=true)) by name, cloud_RoleName | order by count_ desc | take 50",
            ["dependencies"] = "dependencies | summarize count(), failures=countif(success!=true), p95=percentile(duration,95) by target, type, cloud_RoleName | order by count_ desc | take 50",
            ["statusCodes"]  = "requests | summarize count() by resultCode, cloud_RoleName | order by count_ desc",
            ["scaling"]      = "requests | summarize instanceCount=dcount(cloud_RoleInstance) by bin(timestamp, 5m), cloud_RoleName | order by timestamp desc",
            ["performance"]  = "performanceCounters | summarize avg(value) by bin(timestamp, 5m), name, cloud_RoleName, cloud_RoleInstance | order by timestamp desc | take 200",
            ["recentErrors"] = "traces | where severityLevel >= 3 | order by timestamp desc | take 100 | project timestamp, message, severityLevel, cloud_RoleName, cloud_RoleInstance"
        };

        public AzCliAppInsightsBrowser(AppInsightsBrowserOptions options)
        {
            _options = options ?? new AppInsightsBrowserOptions();
        }

        public bool IsAvailable => _options?.Targets != null && _options.Targets.Count > 0;

        public IReadOnlyList<AppInsightsTarget> ListTargets() => _options.Targets ?? new List<AppInsightsTarget>();
        public IReadOnlyList<string> ListPresets() => _presets.Keys.ToList();

        public async Task<AppInsightsQueryResult> QueryAsync(AppInsightsQueryRequest request, CancellationToken ct = default)
        {
            if (request == null) return Err("Request is required.");

            // Normalise to a list of targets — caller may pass either Targets[] (multi) or Target (single).
            var targets = (request.Targets != null && request.Targets.Count > 0)
                ? request.Targets
                : (request.Target != null ? new List<AppInsightsTarget> { request.Target } : null);

            if (targets == null || targets.Count == 0) return Err("At least one target is required.");

            string kql = request.Kql;
            if (string.IsNullOrWhiteSpace(kql))
            {
                if (string.IsNullOrWhiteSpace(request.PresetName) || !_presets.TryGetValue(request.PresetName, out kql))
                    return Err("Either Kql or a recognised PresetName must be supplied. Known presets: " + string.Join(",", _presets.Keys));
            }

            // Run each target's query in parallel; merge rows with a `_target` attribution column.
            var perTarget = await Task.WhenAll(targets.Select(t => RunOneAsync(t, kql, request.Offset, request.MaxRows, ct)));
            return Merge(perTarget, request.MaxRows);
        }

        private async Task<AppInsightsQueryResult> RunOneAsync(AppInsightsTarget t, string kql, string offset, int maxRows, CancellationToken ct)
        {
            var args = BuildArgs(t, kql, offset);
            try
            {
                var (exit, stdout, stderr) = await RunAzAsync(args, ct).ConfigureAwait(false);
                if (exit != 0)
                    return TaggedErr(t, "az exit=" + exit + ": " + (stderr ?? stdout ?? "<no output>"));

                var parsed = ParseResult(stdout, maxRows);
                // Tag every row with the target name so the UI can attribute.
                foreach (var row in parsed.Rows ?? new List<IDictionary<string, object>>())
                    if (!row.ContainsKey("_target")) row["_target"] = t.Name ?? t.AppName ?? "?";
                return parsed;
            }
            catch (FileNotFoundException)
            {
                return TaggedErr(t, "'az' CLI not found on PATH. Install Azure CLI and run `az login`.");
            }
            catch (Exception ex)
            {
                return TaggedErr(t, "Query failed: " + ex.Message);
            }
        }

        private static AppInsightsQueryResult TaggedErr(AppInsightsTarget t, string msg)
            => Err((t.Name ?? t.AppName ?? "?") + ": " + msg);

        private static AppInsightsQueryResult Merge(AppInsightsQueryResult[] results, int maxRows)
        {
            var merged = new AppInsightsQueryResult
            {
                Rows = new List<IDictionary<string, object>>(),
                Columns = new List<string> { "_target" }
            };
            var errs = new List<string>();
            foreach (var r in results)
            {
                if (r == null) continue;
                if (r.IsError) { if (!string.IsNullOrEmpty(r.ErrorMessage)) errs.Add(r.ErrorMessage); continue; }
                foreach (var row in r.Rows ?? new List<IDictionary<string, object>>())
                {
                    merged.Rows.Add(row);
                    if (merged.Rows.Count >= maxRows) break;
                }
                foreach (var c in r.Columns ?? new List<string>())
                    if (!merged.Columns.Contains(c)) merged.Columns.Add(c);
                if (merged.Rows.Count >= maxRows) break;
            }
            merged.RowCount = merged.Rows.Count;
            if (errs.Count > 0 && merged.RowCount == 0)
            {
                merged.IsError = true;
                merged.ErrorMessage = string.Join(" | ", errs);
            }
            return merged;
        }

        private static AppInsightsQueryResult Err(string msg) => new AppInsightsQueryResult { IsError = true, ErrorMessage = msg, Rows = new List<IDictionary<string, object>>(), Columns = new List<string>() };

        private static string BuildArgs(AppInsightsTarget t, string kql, string offset)
        {
            // az on Windows has MSYS path-conv hangups on `/` characters, but we're not
            // passing any path-shaped args here, so a single command line suffices.
            var sb = new StringBuilder();
            sb.Append(" monitor app-insights query");
            if (!string.IsNullOrWhiteSpace(t.SubscriptionId)) sb.Append(" --subscription ").Append(QuoteIfNeeded(t.SubscriptionId));
            if (!string.IsNullOrWhiteSpace(t.AppId))         sb.Append(" --app ").Append(QuoteIfNeeded(t.AppId));
            else
            {
                if (!string.IsNullOrWhiteSpace(t.ResourceGroup)) sb.Append(" -g ").Append(QuoteIfNeeded(t.ResourceGroup));
                if (!string.IsNullOrWhiteSpace(t.AppName))       sb.Append(" -a ").Append(QuoteIfNeeded(t.AppName));
            }
            sb.Append(" --analytics-query ").Append(QuoteIfNeeded(kql));
            if (!string.IsNullOrWhiteSpace(offset)) sb.Append(" --offset ").Append(offset);
            sb.Append(" -o json");
            return sb.ToString();
        }

        private static string QuoteIfNeeded(string s)
        {
            if (s.IndexOfAny(new[] { ' ', '"', '\t' }) < 0) return s;
            return "\"" + s.Replace("\"", "\\\"") + "\"";
        }

        private static async Task<(int exit, string stdout, string stderr)> RunAzAsync(string args, CancellationToken ct)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "az",
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            p.Start();
            var stdoutTask = p.StandardOutput.ReadToEndAsync();
            var stderrTask = p.StandardError.ReadToEndAsync();
            await Task.Run(() => p.WaitForExit(), ct).ConfigureAwait(false);
            var so = await stdoutTask.ConfigureAwait(false);
            var se = await stderrTask.ConfigureAwait(false);
            return (p.ExitCode, so, se);
        }

        private static AppInsightsQueryResult ParseResult(string json, int maxRows)
        {
            try
            {
                var doc = JToken.Parse(json);
                IList<IDictionary<string, object>> rows = new List<IDictionary<string, object>>();
                IList<string> columns = new List<string>();

                if (doc is JArray arr)
                {
                    foreach (var item in arr.Take(maxRows))
                    {
                        var dict = new Dictionary<string, object>();
                        foreach (var prop in (item as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
                        {
                            dict[prop.Name] = prop.Value.Type == JTokenType.Object || prop.Value.Type == JTokenType.Array
                                ? (object)prop.Value.ToString(Newtonsoft.Json.Formatting.None)
                                : prop.Value.ToObject<object>();
                            if (!columns.Contains(prop.Name)) columns.Add(prop.Name);
                        }
                        rows.Add(dict);
                    }
                }
                return new AppInsightsQueryResult
                {
                    Rows = rows,
                    Columns = columns,
                    RowCount = rows.Count,
                    RawJson = json
                };
            }
            catch (Exception ex)
            {
                return Err("Parse failed: " + ex.Message + " — raw: " + (json?.Length > 400 ? json.Substring(0, 400) + "…" : json));
            }
        }
    }

    public class AppInsightsBrowserOptions
    {
        public List<AppInsightsTarget> Targets { get; set; } = new List<AppInsightsTarget>();
    }
}
