using Rxns.AppInsights;
using Rxns.Hosting;

namespace Rxns.AppStatus.Host.AppInsights
{
    /// <summary>
    /// Registers the AppInsights browser surface (used by the portal's AppInsights tab).
    ///
    /// <para>Config sourced from a JSON <c>appstatus.config</c> next to the host binary
    /// (or under <c>RXNS_APPSTATUS_CONFIG_DIR</c>), with optional
    /// <c>appstatus.&lt;env&gt;.config</c> overlay when <c>RXNS_ENV</c> is set.
    /// Multiple targets supported — the UI exposes per-target checkboxes so operators
    /// can enable/disable individual instances and flatten queries across envs.</para>
    ///
    /// <para>Empty / missing cfg → <c>Targets[]</c> empty → the browser reports
    /// <c>IsAvailable=false</c> and the UI shows "no targets configured". Never throws.</para>
    ///
    /// <para>Embedding apps that want richer config (appsettings.json, key vault, etc.)
    /// can register their own <see cref="AppInsightsBrowserOptions"/> in an augment module —
    /// last registration wins.</para>
    /// </summary>
    public class AppInsightsModule : IAppModule
    {
        public IRxnLifecycle Load(IRxnLifecycle lifecycle)
        {
            return lifecycle
                .CreatesOncePerApp(() => AppInsightsRxnCfg.Loader.Resolve())
                .CreatesOncePerApp<AzCliAppInsightsBrowser>()
                ;
        }
    }
}
