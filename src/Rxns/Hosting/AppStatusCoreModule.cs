using System;
using Rxns.Collections;
using Rxns.DDD;
using Rxns.DDD.Commanding;
using Rxns.DDD.CQRS;
using Rxns.Health;
using Rxns.Hosting.Auth;
using Rxns.Hosting.Updates;
using Rxns.Logging;

namespace Rxns.Hosting
{
    /// <summary>
    /// Transport-agnostic services that every Rxns app needs regardless of how
    /// cross-process events flow (HTTP / SignalR / Redis Streams / ...).
    ///
    /// <para>
    /// This module holds the transport-agnostic pieces; one of
    /// <c>HttpTransportModule</c> / <c>SignalRTransportModule</c> /
    /// <c>RedisTransportModule</c> is included alongside it (mutually
    /// exclusive in current usage; combinable in principle). The composition
    /// root picks the transport based on configuration - no last-wins
    /// races, no deferred-registration hacks.
    /// </para>
    ///
    /// <para>Server-only registrations (<c>LocalAppUpdateServer</c>,
    /// <c>CurrentDirectoryAppUpdateStore</c>, <c>AppCommandService</c>,
    /// <c>RespondsToSvcCmds&lt;StreamLogs&gt;</c>) moved to
    /// <see cref="AppStatusServerCoreModule"/> 2026-05 so thin AppStatus *clients*
    /// (adapters that only publish, never serve) don't trip eager-wiring on
    /// <c>IAppStatusCfg</c> / <c>IRxnHostableApp</c> / etc. — those are server
    /// concerns. <see cref="AppStatusClientModule"/> still composites the two
    /// together for back-compat with existing consumers.</para>
    /// </summary>
    public class AppStatusCoreModule : IAppModule
    {
        public IRxnLifecycle Load(IRxnLifecycle lifecycle)
        {
            return lifecycle
                .CreatesOncePerApp<ReporterErrorLogger>()
                .CreatesOncePerApp<InsecureApiNoAuthRequired>()
                .CreateGenericOncePerAppAs(typeof(DomainCommandMetricsWatcher<>), typeof(IDomainCommandPreHandler<>))
                .CreateGenericOncePerAppAs(typeof(DomainQueryMetricsWatcher<>), typeof(IDomainQueryPreHandler<>))
                .CreatesOncePerApp<AppHealthManager>()
                .CreatesOncePerApp<AlreadyLoggedInAsAdminAuthService>()
                .CreatesOncePerApp(() => new ReliableAppThatHeartbeatsEvery(TimeSpan.FromSeconds(10)))
                // Default tuning for persistent-shell partial-result streaming
                // (1s flush window / 20 lines / 16 KB). Consumers resolve this
                // rather than hard-coding the cadence so operators can dial it
                // via DI override without touching the handler.
                .CreatesOncePerApp<IRemoteShellStreamCfg>(_ => new DefaultRemoteShellStreamCfg())
                .CreatesOncePerApp(() => new RxnServiceInfo()
                {
                    Tenant = "NoTenant",
                    Key = "NT"
                })
                .CreatesOncePerApp(() => new ErrorReporterCfg()
                {
                    ErrorReportHistoryLength = 50,
                    MaxErrorsPerSecondBeforeFlood = 20
                })
                .CreatesOncePerApp<EventFactory>()
                .CreatesOncePerApp<LocalRouteInfo>()
                .CreatesOncePerApp<AlreadyLoggedInAsAdminAuthService>(true)
                .CreatesOncePerApp<DotNetFileSystemService>()
                .CreatesOncePerApp<ZipService>()
                .CreatesOncePerApp(() => new AppResourceCfg
                {
                    ThreadPoolSize = 8
                });
        }
    }
}
