using System;
using System.IO;
using Rxns.Hosting;

namespace Rxns.AppStatus.Host
{
    /// <summary>
    /// Knobs the host app exposes for any rxns app embedding the AppStatus portal.
    ///
    /// <para>
    /// The portal is the same Angular SPA that ships under <c>Rxns.AppSatus/Web</c>.
    /// At runtime we serve its built output (<c>dist/</c>) and the
    /// <c>Rxns.WebApiNET5</c> REST + SignalR endpoints (<c>/api/appstatus/*</c>,
    /// <c>/appStatusLogHub</c>, <c>/api/claude/*</c>, <c>/api/appinsights/*</c>).
    /// </para>
    /// </summary>
    public class AppStatusHostCfg : IWebApiCfg, Rxns.Hosting.IAugmentableCfg
    {
        /// <summary>
        /// Canonical port. <see cref="BindingUrl"/> derives from this when not
        /// explicitly set (see <see cref="WebApiCfgExtensions.ResolveBindingUrl"/>).
        /// </summary>
        public int Port { get; set; } = 888;

        /// <summary>
        /// Optional explicit Kestrel binding URLs. Comma-separated to bind multiple
        /// endpoints. When null/empty, <c>http://*:{Port}</c> is used.
        /// </summary>
        public string BindingUrl { get; set; }

        /// <summary>
        /// Disk path to the AppStatus portal's built SPA — the <c>dist/</c> folder
        /// produced by <c>Rxns.AppSatus/Web/build.mjs</c>. If null/empty the host
        /// probes a small list of well-known relative locations (see
        /// <see cref="AppStatusPortal.ResolveHtml5Root"/>).
        /// </summary>
        public string Html5Root { get; set; }

        /// <inheritdoc/>
        public string Html5IndexHtml { get; set; } = "index.html";

        /// <summary>
        /// SystemName advertised by this host process to AppStatus. Optional —
        /// defaults to the embedding assembly name when null.
        /// </summary>
        public string SystemName { get; set; }

        /// <summary>
        /// Human-readable label for this portal exposed via portal discovery
        /// (pill bar / SSDP friendly name). Defaults to <see cref="SystemName"/>.
        /// </summary>
        public string FriendlyName { get; set; }

        public bool EnableSsdp { get; set; } = true;

        /// <summary>
        /// Disk path to an augmenting host's static-file overlay, mounted by the
        /// portal at <c>/augment/*</c>. The base SPA's <c>index.html</c> loads
        /// <c>/augment/init.js</c> with <c>onerror=this.remove()</c>; augment-host
        /// projects (e.g. <c>YourApp.SupportPortal</c>) set this to their
        /// <c>Web/</c> folder so their <c>init.js</c> + per-augment partial
        /// folders are served. Null/empty/missing = no augment mounted, base
        /// SPA behaves identically to a non-augmented host.
        /// </summary>
        /// <remarks>Augment-host projects ship as a consumer package (e.g.
        /// <c>YourApp.SupportPortal</c>).</remarks>
        public string AugmentRoot { get; set; }
    }

    internal static class HostCfgDefaults
    {
        /// <summary>
        /// Best-effort probe for the portal's built <c>dist/</c> next to a typical
        /// rxns repo layout: <c>&lt;app&gt;\..\..\rxns\Rxns.AppSatus\Web\dist</c>
        /// or sibling-of-app checkouts. First hit wins.
        /// </summary>
        public static string ProbeHtml5Root()
        {
            var here = AppContext.BaseDirectory;
            var candidates = new[]
            {
                Path.Combine(here, "wwwroot"),
                Path.Combine(here, "appstatus-portal"),
                // common cross-repo layouts (sibling consumer checkouts / etc.)
                Path.Combine(here, "..", "..", "..", "..", "..", "rxns", "Rxns.AppSatus", "Web", "dist"),
                Path.Combine(here, "..", "..", "..", "..", "rxns", "Rxns.AppSatus", "Web", "dist"),
                Path.Combine(here, "..", "..", "..", "rxns", "Rxns.AppSatus", "Web", "dist"),
                Path.Combine(here, "..", "..", "rxns", "Rxns.AppSatus", "Web", "dist"),
                // intra-rxns layout: bin\Debug\net10.0 -> ..\..\..\..\Rxns.AppSatus\Web\dist
                Path.Combine(here, "..", "..", "..", "..", "Rxns.AppSatus", "Web", "dist"),
            };
            foreach (var c in candidates)
            {
                try
                {
                    var full = Path.GetFullPath(c);
                    if (Directory.Exists(full)) return full;
                }
                catch { /* ignore — try the next candidate */ }
            }
            return null;
        }
    }
}
