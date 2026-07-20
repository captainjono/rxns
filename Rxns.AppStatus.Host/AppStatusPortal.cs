using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Rxns.AppStatus.Host.Discovery;
using Rxns.Cloud;
using Rxns.Hosting;
using Rxns.Logging;
using Rxns.WebApiNET5.NET5WebApiAdapters;

namespace Rxns.AppStatus.Host
{
    /// <summary>
    /// Public entry point for hosting the AppStatus portal alongside any rxns app.
    ///
    /// <para>Usage from any rxns app's <c>Main</c>:</para>
    /// <code>
    /// // (existing) the app's own web on :5050
    /// _ = AspNetCoreWebApiAdapter.StartWebServices&lt;MyApp&gt;(cfg, args);
    ///
    /// // AppStatus portal on :5060 in the same process
    /// await AppStatusPortal.StartAsync(new AppStatusHostCfg
    /// {
    ///     BindingUrl = "http://*:5060",
    ///     SystemName = "myapp"
    /// });
    /// </code>
    ///
    /// <para>Pass <see cref="IAppModule"/>s via the <c>augment</c> params to layer
    /// your own registrations (e.g. extra Claude tools) into the portal's container
    /// without touching the host project.</para>
    /// </summary>
    public static class AppStatusPortal
    {
        private static IDisposable _hostHandle;
        private static readonly object _gate = new object();

        /// <summary>
        /// Start the portal on the configured binding. Returns once the underlying
        /// <see cref="AspNetCoreWebApiAdapter.StartWebServices{T}"/> task resolves
        /// (which it does after host shutdown), so callers normally don't <c>await</c>
        /// this directly — assign the task and let it run with the rest of the process.
        /// </summary>
        /// <param name="cfg">Host config (binding URL, portal dist path, system name).</param>
        /// <param name="augment">Extra modules to load into the portal's container.</param>
        public static Task StartAsync(AppStatusHostCfg cfg, params IAppModule[] augment)
        {
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));

            // Resolve a sensible Html5Root if the caller didn't pin one.
            if (string.IsNullOrWhiteSpace(cfg.Html5Root))
                cfg.Html5Root = ResolveHtml5Root();

            // Hand cfg + augments to the bootstrap app via its statics. We use the
            // statics-on-app pattern because Rxns.WebApiNET5.AspNetCoreWebApiAdapter
            // exposes a `StartWebServices<TApp>` shape that constructs TApp itself.
            AppStatusHostApp.Cfg = cfg;
            // PRESERVE any pre-existing entries (a consumer app's
            // Program.Main can add modules to Augments
            // before StartAsync so they can also register MVC ApplicationParts
            // via Rxns.WebApiNET5.ConfigureAndStartAspnetCore.ExtraControllerAssemblies
            // in the same place). Just APPEND the params here so the API still
            // works for the original "one-shot" callers.
            if (AppStatusHostApp.Augments == null)
                AppStatusHostApp.Augments = new List<IAppModule>();
            if (augment != null)
            {
                foreach (var m in augment) AppStatusHostApp.Augments.Add(m);
            }

            $"AppStatusPortal starting on {cfg.ResolveBindingUrl()} (Html5Root={cfg.Html5Root ?? "<none>"})".LogDebug();

            WireDiscovery(cfg);

            var run = AspNetCoreWebApiAdapter
                .StartWebServices<AppStatusHostApp>(cfg)
                .ContinueWith(t =>
                {
                    lock (_gate)
                    {
                        _hostHandle = t.IsCompletedSuccessfully ? t.Result : null;
                    }
                }, TaskScheduler.Default);

            return run;
        }

        /// <summary>
        /// Stop the portal (best-effort). Safe to call repeatedly.
        /// </summary>
        public static void Stop()
        {
            IDisposable handle;
            lock (_gate)
            {
                handle = _hostHandle;
                _hostHandle = null;
            }
            try { handle?.Dispose(); } catch { /* ignore — process is going away */ }
        }

        /// <summary>
        /// Resolve the portal's built <c>dist/</c> via <see cref="HostCfgDefaults.ProbeHtml5Root"/>.
        /// Exposed so callers can probe + override the result before constructing cfg.
        /// </summary>
        public static string ResolveHtml5Root() => HostCfgDefaults.ProbeHtml5Root();

        private static IDisposable _ssdpAdvertise;

        private static void WireDiscovery(AppStatusHostCfg cfg)
        {
            try
            {
                var selfUrl = cfg.LocalUrl();
                var name = cfg.FriendlyName ?? cfg.SystemName ?? "rxns-portal";
                var augments = (AppStatusHostApp.Augments ?? new List<IAppModule>())
                    .Where(a => a != null)
                    .Select(a => a.GetType().Name)
                    .ToArray();

                PortalDiscovery.SelfUrl = selfUrl;
                PortalDiscovery.SelfName = name;
                PortalDiscovery.SelfAugments = augments;
                PortalDiscovery.DataDir = ResolveDataDir();

                if (cfg.EnableSsdp)
                {
                    var ssdp = new SsdpPeerCache();
                    ssdp.Start();
                    PortalDiscovery.SsdpCache = ssdp;

                    var disco = new SsdpDiscoveryService();
                    _ssdpAdvertise = disco
                        .Advertise("rxns", "support-portal", selfUrl, friendlyName: name)
                        .Subscribe(_ => { }, e => ("ssdp advertise error: " + e.Message).LogDebug("AppStatusPortal"));
                    ("ssdp: advertising " + selfUrl + " as " + name).LogDebug("AppStatusPortal");
                }

                ("portal discovery: dataDir=" + PortalDiscovery.DataDir).LogDebug("AppStatusPortal");
            }
            catch (Exception e)
            {
                ("portal discovery wire failed: " + e.Message).LogDebug("AppStatusPortal");
            }
        }

        private static string ResolveDataDir()
        {
            var cwd = Directory.GetCurrentDirectory();
            return Path.Combine(cwd, ".rxns");
        }
    }
}
