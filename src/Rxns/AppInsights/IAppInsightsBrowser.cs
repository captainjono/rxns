using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Rxns.AppInsights
{
    public class AppInsightsTarget
    {
        public string Name { get; set; }                 // friendly label e.g. "insights-prod"
        public string SubscriptionId { get; set; }
        public string ResourceGroup { get; set; }
        public string AppName { get; set; }              // AI component name
        public string AppId { get; set; }                // optional — used when the resource path is awkward
        public bool DefaultEnabled { get; set; } = true; // initial UI checkbox state — users can override per-session
    }

    public class AppInsightsQueryRequest
    {
        /// <summary>
        /// One or more targets to query. Backend issues the same KQL against each enabled
        /// target and merges rows with a `_target` column prepended for attribution.
        /// When empty/null, the browser falls back to <see cref="Target"/>.
        /// </summary>
        public System.Collections.Generic.List<AppInsightsTarget> Targets { get; set; }

        /// <summary>Single-target compat path. Ignored when <see cref="Targets"/> is populated.</summary>
        public AppInsightsTarget Target { get; set; }

        public string Kql { get; set; }                   // raw KQL — caller already wrote it
        public string PresetName { get; set; }            // when set, browser maps to a built-in KQL
        public string Offset { get; set; } = "24h";       // e.g. "1h" / "24h" / "7d"
        public int MaxRows { get; set; } = 200;
    }

    public class AppInsightsQueryResult
    {
        public IList<IDictionary<string, object>> Rows { get; set; }
        public IList<string> Columns { get; set; }
        public int RowCount { get; set; }
        public bool IsError { get; set; }
        public string ErrorMessage { get; set; }
        public string RawJson { get; set; }               // for the UI to show diagnostically
    }

    /// <summary>
    /// Generic browse surface over a configured Application Insights component.
    /// Decoupled from any insights-specific code: the rxns-support portal calls
    /// this; concrete implementations (e.g. via <c>az monitor app-insights query</c>)
    /// run inside the portal's own host. Insights provides nothing more than the
    /// connection metadata via the support-portal adapter wire.
    /// </summary>
    public interface IAppInsightsBrowser
    {
        bool IsAvailable { get; }
        IReadOnlyList<AppInsightsTarget> ListTargets();
        IReadOnlyList<string> ListPresets();
        Task<AppInsightsQueryResult> QueryAsync(AppInsightsQueryRequest request, CancellationToken ct = default);
    }
}
