using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using Janison.Micro;
using Rxns.Collections;
using Rxns.DDD;
using Rxns.DDD.Commanding;
using Rxns.DDD.CQRS;
using Rxns.Health;
using Rxns.Hosting.Auth;
using Rxns.Hosting.Compression;
using Rxns.Hosting.Updates;
using Rxns.Logging;
using Rxns.WebApi.Compression;

namespace Rxns.Hosting
{
    public class AppStatusClientModule : IAppModule
    {
        public IRxnLifecycle Load(IRxnLifecycle lifecycle)
        {
            return lifecycle
                .CreatesOncePerApp(c =>
                {
                    var cfg = c.Resolve<HttpClientCfg>();
                    var client = new HttpClient(new TransferCompressionHandler(c.Resolve<IRxnHealthManager>(), cfg, new GZipCompressor()));

                    
                    client.DefaultRequestHeaders.Accept.Clear();
                    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    client.Timeout = cfg.TotalTransferTimeout;
                    
                    return client;
                })
                .CreatesOncePerAppNamed<AnonymousHttpConnection, IHttpConnection>("anonymous")
                .CreatesOncePerAppNamed<AuthenticatedHttpConnection, IHttpConnection>("authenticated")
                .CreatesOncePerApp(c =>
                {
                    var cfg = c.Resolve<IAppServiceRegistry>();

                    return new CommandServiceClientCfg()
                    {
                        BaseUrl = cfg.AppStatusUrl
                    };
                }, true)
                .CreatesOncePerApp(cc =>
                {
                    var anonConnection = cc.ResolveTag<IHttpConnection>("anonymous");
                    var authedConnection = cc.ResolveTag<IHttpConnection>("authenticated");
                    var credentials = cc.Resolve<ITenantCredentials>();
                    var cfg = cc.Resolve<IAppServiceRegistry>();

                    return new CommandServiceClient(cfg, authedConnection, anonConnection, credentials);
                })
                .CreatesOncePerApp<AppSystemStatusPublisher>()
                .CreatesOncePerApp<LocalAppStatusServer>()
                
                .CreatesOncePerApp<SystemStatusService>()
                .CreatesOncePerApp<ReporterErrorLogger>()
                .CreatesOncePerApp<SystemStatusPublisher>()
                .RespondsToSvcCmds<StreamLogs>()
                .CreatesOncePerApp<HttpEventsServiceClient>()
                .CreatesOncePerApp<HttpAppStatusServiceClient>()
                .CreatesOncePerApp<HttpUpdateServiceClient>()
                .CreateGenericOncePerAppAs(typeof(DomainCommandMetricsWatcher<>), typeof(IDomainCommandPreHandler<>))
                .CreateGenericOncePerAppAs(typeof(DomainQueryMetricsWatcher<>), typeof(IDomainQueryPreHandler<>))
                .CreatesOncePerApp<AppHealthManager>()

                .CreatesOncePerApp<AlreadyLoggedInAsAdminAuthService>()
                .CreatesOncePerApp(() => new ReliableAppThatHeartbeatsEvery(TimeSpan.FromSeconds(10)))
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
                .CreatesOncePerApp<ClientAppStatusErrorChannel>()
                .CreatesOncePerApp<DotNetFileSystemService>()
                .CreatesOncePerApp<AppUpdateServiceClient>()
                .CreatesOncePerApp<HttpUpdateServiceClient>()
                .CreatesOncePerApp<LocalAppUpdateServer>()
                .CreatesOncePerApp<CurrentDirectoryAppUpdateStore>()
                .CreatesOncePerApp<ZipService>()
                .CreatesOncePerApp(() => new AppResourceCfg
                {
                    ThreadPoolSize = 8
                })
                .CreatesOncePerApp(() => new HttpClientCfg()
                {
                    TotalTransferTimeout = TimeSpan.FromMinutes(5),
                    EnableCompression = true
                })
                .CreatesOncePerApp<AppCommandService>();
        }
    }
}
