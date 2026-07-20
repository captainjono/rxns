using System;
using System.Collections.Generic;
using System.IO;
using Rxns.Health.AppStatus;
using Rxns.Hosting;
using Rxns.Hosting.Updates;
using Rxns.Logging;
using Rxns.NewtonsoftJson;
using Rxns.Playback;
using Rxns.WebApiNET5;
using Rxns.WebApiNET5.NET5WebApiAdapters;

namespace Rxns.AppStatus.Host
{
    /// <summary>
    /// Minimal Rxns app definition that boots only what the AppStatus portal needs:
    /// the AppStatus server module (in-memory log/error/stats stores +
    /// <see cref="LocalAppStatusManager"/> / <see cref="LocalAppStatusLogReader"/>),
    /// plus whatever <see cref="IAppModule"/>s the embedding host passes in via
    /// <see cref="AppStatusPortal.StartAsync"/>.
    ///
    /// <para>The REST controllers and SignalR hubs live in <c>Rxns.WebApiNET5</c>
    /// and are mapped by <see cref="ConfigureAndStartAspnetCore"/> — we don't need
    /// to register them explicitly. Controllers in this assembly
    /// (<c>AiChatController</c>, <c>AppInsightsController</c>) are picked up by
    /// MVC's controller feature provider when this assembly is loaded.</para>
    /// </summary>
    public class AppStatusHostApp : ConfigureAndStartAspnetCore
    {
        // Static so the public AppStatusPortal facade can wire cfg + modules
        // before the host instantiates this app via the generic StartWebServices<T>.
        public static AppStatusHostCfg Cfg { get; set; } = new AppStatusHostCfg();
        public static IList<IAppModule> Augments { get; set; } = new List<IAppModule>();

        public override Func<string, Action<IRxnLifecycle>> App { get; } = url => lifecycle =>
        {
            // Match bfg's proven AppStatus-server loadout (theBFGDef.TestArena lambda).
            // bfg IS the canonical "self-hosted arena that exposes the portal" — same
            // shape this host needs. The earlier attempts to slim this down kept hitting
            // cascading DI failures because the full module graph is what satisfies the
            // ctor chain of LocalAppStatusManager / LocalAppUpdateServer / etc.
            lifecycle.Includes<RxnsModule>();
            lifecycle.Includes<AppStatusClientModule>();      // Core + HTTP transport
            lifecycle.CreatesOncePerApp<NoOpAppStatusServiceClient>();  // self-host: don't loop publishes back to ourselves
            lifecycle.CreatesOncePerApp<LocalAppStatusManager>();
            lifecycle.CreatesOncePerApp<Rxns.Hosting.Updates.FileSystemAppUpdateRepo>();
            lifecycle.CreatesOncePerApp<Rxns.Hosting.Updates.NestedInAppDirAppUpdateStore>();
            lifecycle.Includes<AspNetCoreWebApiAdapterModule>();

            // IAppStatusCfg — InMemoryAppStatusStore needs one. Ground its AppRoot
            // under the host's bin dir so tape/log files land next to the binary.
            var appRoot = Path.Combine(System.AppContext.BaseDirectory, ".bfg");
            lifecycle.CreatesOncePerApp<IAppStatusCfg>(() => new AppStatusCfg { AppRoot = appRoot, ShouldAutoUnzipLogs = false });

            // IStringCodec — AppStatusServerModule's tape repositories need a codec.
            // Newtonsoft JSON is the canonical rxns wire format.
            lifecycle.CreatesOncePerApp<IStringCodec>(() => new FromJsonCodec());

            // IAppCmdManager — LocalAppStatusManager's ctor wants one (phase 7n4 refactor).
            // RoutableAppCmdManager is the only impl; normally registered by hub modules.
            lifecycle.CreatesOncePerApp<Rxns.Hosting.Updates.RoutableAppCmdManager>();


            // Core AppStatus pipeline — gives us LocalAppStatusManager + the log reader the
            // /api/appstatus/* controller and /appStatusLogHub hub both depend on.
            lifecycle.Includes<AppStatusServerModule>();

            // Bridge POST /events/publish RLMs → ReportStatus.Log → store.
            // RemoteReportStatusEcho is an IRxnProcessor<RLM>; without it
            // registered the EventController accepts cross-process RLMs (HTTP 200,
            // "published N events") but no subscriber forwards them to the
            // LocalAppStatusManager subscription, so they vanish silently. This
            // is the missing piece that makes the adapter's StreamLogs shipments
            // visible in /api/appstatus/log and the supportInsights dashboard.
            lifecycle.CreatesOncePerApp<Rxns.Logging.RemoteReportStatusEcho>();

            // Built-in modules for the controllers shipped alongside the portal.
            lifecycle.Includes<Ai.AiModule>();
            lifecycle.Includes<AppInsights.AppInsightsModule>();
            lifecycle.Includes<Monitor.MonitorModule>();

            // Diagnostic surface — captures the container's resolver after build so
            // in-process tests can inspect IAppContainer / InMemoryAppStatusStore.
            lifecycle.CreatesOncePerApp<AppStatusHostDiagnostics>();

            // Caller-supplied augmentations layer on top. Used by embedding apps
            // (consumer/augment projects) to register their own AI tools,
            // app-specific health views, etc.
            ("applying " + (Augments?.Count ?? 0) + " augments").LogDebug("AppStatusHostApp");
            foreach (var m in Augments ?? new List<IAppModule>())
            {
                if (m == null) continue;
                ("loading augment: " + m.GetType().FullName).LogDebug("AppStatusHostApp");
                m.Load(lifecycle);
            }
        };

        public override IRxnAppInfo AppInfo { get; } = new AppVersionInfo(
            name: !string.IsNullOrWhiteSpace(Cfg?.SystemName) ? Cfg.SystemName : "Rxns.AppStatus.Host",
            version: "1.0",
            keepUptoDate: false);

        public override IWebApiCfg WebApiCfg => Cfg ?? new AppStatusHostCfg();
    }
}
