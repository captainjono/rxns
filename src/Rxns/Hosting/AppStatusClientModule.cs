using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reactive;
using System.Reactive.Subjects;
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

    public class InsecureApiNoAuthRequired : ReportStatus, IAuthenticationService<AccessToken, ITenantCredentials>
    {
        public ITenantCredentials Credentials { get; set; }
        public bool RequiresSSL { get; set; }
        public IObservable<AccessToken> Tokens { get; } = _noToken;
        public IObservable<bool> IsAuthenticated { get; } = _alwaysAuthed;

        private static ISubject<bool> _alwaysAuthed = new BehaviorSubject<bool>(true);
        private static ISubject<AccessToken> _noToken = new BehaviorSubject<AccessToken>(new AccessToken());
        public IObservable<AccessToken> Refresh()
        {
            return new AccessToken().ToObservable();
        }

        public IObservable<AccessToken> Login(ITenantCredentials credentials)
        {
            return Refresh();
        }

        public IObservable<AccessToken> GetToken(ITenantCredentials credentials)
        {
            return Refresh();
        }
    }

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
                    //client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/zip"));
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
                }, true)
                .CreatesOncePerApp<AppSystemStatusPublisher>()
                .CreatesOncePerApp<LocalAppStatusServer>()
                
                .CreatesOncePerApp<SystemStatusService>()
                .CreatesOncePerApp<ReporterErrorLogger>()
                .CreatesOncePerApp<SystemStatusPublisher>()
                .RespondsToSvcCmds<StreamLogs>()
                .CreatesOncePerApp<InsecureApiNoAuthRequired>()
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
                //.CreatesOncePerApp<ClientAppStatusErrorChannel>()
                .CreatesOncePerApp<DotNetFileSystemService>()
                .CreatesOncePerApp<AppUpdateServiceClient>()
                .CreatesOncePerApp<HttpUpdateServiceClient>()
                .CreatesOncePerApp<LocalAppUpdateServer>()
                .CreatesOncePerApp<CurrentDirectoryAppUpdateStore>(preserveExisting: true)
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
