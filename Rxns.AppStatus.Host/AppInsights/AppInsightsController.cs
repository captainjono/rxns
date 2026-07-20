using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using Rxns.AppInsights;

namespace Rxns.AppStatus.Host.AppInsights
{
    /// <summary>
    /// REST surface that the rxns-support portal's Errors page uses for the
    /// "AppInsights" tab. Lists configured targets, lists built-in KQL presets,
    /// and runs a query. Browser implementation is swapped via DI — current
    /// concrete is <see cref="AzCliAppInsightsBrowser"/>.
    /// </summary>
    [ApiController]
    [Route("api/appinsights")]
    public class AppInsightsController : ControllerBase
    {
        private readonly IAppInsightsBrowser _browser;
        // Shared HttpClient for the direct REST fallback. Single instance is fine
        // — these are short-lived synchronous calls and we want connection reuse.
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

        public AppInsightsController(IAppInsightsBrowser browser) { _browser = browser; }

        [HttpGet("info")]
        public IActionResult Info() => Ok(new
        {
            available = _browser.IsAvailable,
            targets = _browser.ListTargets(),
            presets = _browser.ListPresets()
        });

        [HttpPost("query")]
        public async Task<IActionResult> Query([FromBody] AppInsightsQueryRequest request, CancellationToken ct)
        {
            if (request == null) return BadRequest("request body required");
            var r = await _browser.QueryAsync(request, ct).ConfigureAwait(false);
            if (r.IsError) return StatusCode(502, r);
            return Ok(r);
        }

        /// <summary>
        /// REST fallback for when we have a raw instrumentation key / Log Analytics
        /// AppId + API key but no Subscription / RG / AppName for `az monitor
        /// app-insights query`. Hits the Application Insights REST API at
        /// <c>https://api.applicationinsights.io/v1/apps/{appId}/query</c>.
        /// <para>The caller must already have an Application Insights API key
        /// generated against the AppId — devs typically configure these per
        /// machine in insights' Web.config (<c>AppInsights.Web.ClsApplicationID</c>
        /// + <c>AppInsights.Web.ClsAPIKey</c>). The portal's Infra resolver
        /// surfaces them; this endpoint takes them from the caller verbatim
        /// rather than re-reading config so the same wire works in any host.</para>
        /// </summary>
        [HttpPost("query-direct")]
        public async Task<IActionResult> QueryDirect([FromBody] AppInsightsDirectQueryRequest request, CancellationToken ct)
        {
            if (request == null) return BadRequest("request body required");
            if (string.IsNullOrWhiteSpace(request.AppId)) return BadRequest("AppId required (Log Analytics Application Id)");
            if (string.IsNullOrWhiteSpace(request.ApiKey)) return BadRequest("ApiKey required");
            var kql = request.Kql;
            if (string.IsNullOrWhiteSpace(kql)) return BadRequest("Kql required");

            // The REST API takes timespan as ISO-8601 duration (PT1H) — translate
            // the convenience suffixes the az-cli path accepts (1h / 24h / 7d).
            var timespan = TranslateOffsetToIso8601(request.Offset ?? "24h");

            var body = new JObject { ["query"] = kql };
            if (!string.IsNullOrWhiteSpace(timespan)) body["timespan"] = timespan;

            var url = "https://api.applicationinsights.io/v1/apps/" + Uri.EscapeDataString(request.AppId.Trim()) + "/query";
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(body.ToString(), Encoding.UTF8, "application/json")
            };
            req.Headers.Add("x-api-key", request.ApiKey.Trim());

            try
            {
                using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
                var raw = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    return StatusCode(502, new AppInsightsQueryResult
                    {
                        IsError = true,
                        ErrorMessage = "AI REST status=" + (int)resp.StatusCode + ": " + (raw?.Length > 400 ? raw.Substring(0, 400) + "…" : raw),
                        Rows = new List<IDictionary<string, object>>(),
                        Columns = new List<string>(),
                        RawJson = raw
                    });
                }
                var parsed = ParseAiRestResult(raw, request.MaxRows > 0 ? request.MaxRows : 200);
                return Ok(parsed);
            }
            catch (Exception ex)
            {
                return StatusCode(502, new AppInsightsQueryResult
                {
                    IsError = true,
                    ErrorMessage = "AI REST call failed: " + ex.Message,
                    Rows = new List<IDictionary<string, object>>(),
                    Columns = new List<string>()
                });
            }
        }

        /// <summary>
        /// AI REST response shape:
        /// <c>{ tables: [{ name, columns: [{name,type}], rows: [[...], ...] }, ...] }</c>.
        /// We collapse the first table (the query result; subsequent tables are
        /// metadata) into the same <see cref="AppInsightsQueryResult"/> shape the
        /// az-cli path returns so the UI doesn't need to branch.
        /// </summary>
        private static AppInsightsQueryResult ParseAiRestResult(string json, int maxRows)
        {
            var rows = new List<IDictionary<string, object>>();
            var columns = new List<string>();
            try
            {
                var doc = JObject.Parse(json);
                var tables = doc["tables"] as JArray;
                if (tables == null || tables.Count == 0)
                {
                    return new AppInsightsQueryResult { Rows = rows, Columns = columns, RowCount = 0, RawJson = json };
                }
                var table = tables[0] as JObject;
                var cols = table?["columns"] as JArray ?? new JArray();
                foreach (var col in cols)
                {
                    var n = col?["name"]?.ToString();
                    if (!string.IsNullOrEmpty(n)) columns.Add(n);
                }
                var dataRows = table?["rows"] as JArray ?? new JArray();
                var take = Math.Min(maxRows, dataRows.Count);
                for (var i = 0; i < take; i++)
                {
                    var arr = dataRows[i] as JArray;
                    if (arr == null) continue;
                    var dict = new Dictionary<string, object>();
                    for (var ci = 0; ci < columns.Count && ci < arr.Count; ci++)
                    {
                        var v = arr[ci];
                        dict[columns[ci]] = v == null || v.Type == JTokenType.Null
                            ? null
                            : (v.Type == JTokenType.Object || v.Type == JTokenType.Array
                                ? (object)v.ToString(Newtonsoft.Json.Formatting.None)
                                : v.ToObject<object>());
                    }
                    rows.Add(dict);
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
                return new AppInsightsQueryResult
                {
                    IsError = true,
                    ErrorMessage = "Parse failed: " + ex.Message,
                    Rows = rows,
                    Columns = columns,
                    RawJson = json
                };
            }
        }

        private static string TranslateOffsetToIso8601(string offset)
        {
            if (string.IsNullOrWhiteSpace(offset)) return null;
            offset = offset.Trim();
            // Already ISO-8601?
            if (offset.StartsWith("P", StringComparison.OrdinalIgnoreCase)) return offset;
            // suffix forms: 30m / 1h / 24h / 7d
            var n = 0; var i = 0;
            while (i < offset.Length && char.IsDigit(offset[i])) { n = n * 10 + (offset[i] - '0'); i++; }
            if (n == 0) return null;
            var unit = i < offset.Length ? char.ToLowerInvariant(offset[i]) : 'h';
            switch (unit)
            {
                case 'm': return "PT" + n + "M";
                case 'h': return "PT" + n + "H";
                case 'd': return "P" + n + "D";
                default: return "PT" + n + "H";
            }
        }
    }

    /// <summary>
    /// Body for <c>POST /api/appinsights/query-direct</c>. The caller already
    /// has the AppId + ApiKey (e.g. from the per-component resolver). Kept
    /// separate from <see cref="AppInsightsQueryRequest"/> so we don't pollute
    /// the az-cli surface with REST-only fields.
    /// </summary>
    public class AppInsightsDirectQueryRequest
    {
        /// <summary>Application Insights Log Analytics application ID (GUID). Maps to <c>AppInsights.Web.ClsApplicationID</c>.</summary>
        public string AppId { get; set; }
        /// <summary>API key generated against the AppId. Maps to <c>AppInsights.Web.ClsAPIKey</c>.</summary>
        public string ApiKey { get; set; }
        public string Kql { get; set; }
        /// <summary>Convenience suffix form: <c>1h</c> / <c>24h</c> / <c>7d</c> — translated to ISO-8601 server-side.</summary>
        public string Offset { get; set; } = "24h";
        public int MaxRows { get; set; } = 200;
    }
}
