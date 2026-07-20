using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rxns.Ai;
using Rxns.AppInsights;

namespace Rxns.AppStatus.Host.Ai.Tools
{
    /// <summary>
    /// Read-only tool — let the model run KQL against configured AppInsights
    /// instances via <see cref="IAppInsightsBrowser"/>. Targets the same
    /// configured set the portal's AppInsights tab uses (loaded from
    /// <c>appstatus.config</c>). Caller picks targets by name; omit to run
    /// across every configured target (flattened with <c>_target</c> attribution).
    /// </summary>
    public class QueryAppInsightsTool : IAiToolHandler
    {
        private readonly IAppInsightsBrowser _browser;

        public QueryAppInsightsTool(IAppInsightsBrowser browser)
        {
            _browser = browser;
        }

        public AiToolDefinition Definition { get; } = new AiToolDefinition
        {
            Name = "query_appinsights",
            Description = "Run KQL against the host's configured AppInsights instances. Use when the user asks about latency, top endpoints, exceptions, dependencies, status codes, or scaling. Preset names: latency, exceptions, dependencies, performance, statusCodes, scaling, topEndpoints, recentErrors. Free-form KQL also accepted.",
            RequiresWriteAccess = false,
            InputSchemaJson = @"{
  ""type"": ""object"",
  ""properties"": {
    ""targetNames"": {
      ""type"": ""array"", ""items"": { ""type"": ""string"" },
      ""description"": ""Subset of configured target Names (e.g. ['ase-insights', 'insights-prod']). Omit to query every configured target.""
    },
    ""presetName"": { ""type"": ""string"", ""description"": ""One of: latency, exceptions, dependencies, performance, statusCodes, scaling, topEndpoints, recentErrors. Either presetName or kql required."" },
    ""kql"":        { ""type"": ""string"", ""description"": ""Free-form KQL. Overrides presetName when both supplied."" },
    ""offset"":     { ""type"": ""string"", ""description"": ""Lookback window (e.g. '1h', '24h', '7d'). Default '24h'."" },
    ""maxRows"":    { ""type"": ""integer"", ""description"": ""Max rows merged across targets (default 100, hard cap 500)."" }
  }
}"
        };

        public async Task<AiToolResult> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
        {
            try
            {
                if (_browser == null || !_browser.IsAvailable)
                {
                    return new AiToolResult
                    {
                        OutputJson = JsonConvert.SerializeObject(new
                        {
                            available = false,
                            note = "No AppInsights targets configured. Drop an `appstatus.config` next to the host with a Targets[] array, or set RXNS_APPSTATUS_CONFIG_DIR."
                        })
                    };
                }

                var args = string.IsNullOrWhiteSpace(argumentsJson) ? new JObject() : JObject.Parse(argumentsJson);
                var preset = (string)args["presetName"];
                var kql = (string)args["kql"];
                var offset = (string)args["offset"] ?? "24h";
                int maxRows = args["maxRows"] != null ? (int)args["maxRows"] : 100;
                if (maxRows > 500) maxRows = 500;

                var allTargets = _browser.ListTargets();
                var wanted = (args["targetNames"] as JArray)?.Select(t => (string)t).ToList();
                var targets = (wanted != null && wanted.Count > 0)
                    ? allTargets.Where(t => wanted.Any(w => string.Equals(w, t.Name, StringComparison.OrdinalIgnoreCase))).ToList()
                    : allTargets.ToList();

                if (targets.Count == 0)
                {
                    return new AiToolResult
                    {
                        OutputJson = JsonConvert.SerializeObject(new
                        {
                            available = true,
                            error = "No targets matched. Configured: " + string.Join(",", allTargets.Select(t => t.Name))
                        })
                    };
                }

                var request = new AppInsightsQueryRequest
                {
                    Targets = targets.ToList(),
                    PresetName = preset,
                    Kql = kql,
                    Offset = offset,
                    MaxRows = maxRows
                };

                var result = await _browser.QueryAsync(request, ct).ConfigureAwait(false);
                return new AiToolResult { OutputJson = JsonConvert.SerializeObject(result) };
            }
            catch (Exception ex)
            {
                return new AiToolResult { IsError = true, ErrorMessage = ex.Message };
            }
        }
    }
}
