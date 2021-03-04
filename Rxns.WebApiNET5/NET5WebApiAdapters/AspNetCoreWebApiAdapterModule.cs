using Rxns.DDD;
using Rxns.Health.AppStatus;
using Rxns.Hosting;
using Rxns.Logging;
using Rxns.WebApiNET5.NET5WebApiAdapters.RxnsApiAdapters;

namespace Rxns.WebApiNET5.NET5WebApiAdapters
{
    public class AspNetCoreWebApiAdapterModule : IAppModule
    {
        public IRxnLifecycle Load(IRxnLifecycle lifecycle)
        {
            
            return lifecycle
                .CreatesOncePerApp<ReportHub>()
                .CreatesOncePerApp<SystemMetricsHub>()
                .CreatesOncePerApp<EventsHub>()
                .CreatesOncePerRequest<EventController>()
                .CreatesOncePerApp<RemoteReportStatusEcho>()
                .CreatesOncePerApp<SystemMetricsReport>()
                .CreatesOncePerApp<AspNetCoreWebApiAdapter>()
                //.CreatesOncePerApp<HostBuffer>()

                //.CreatesOncePerAppAs<NoOAuthAuthentication, OAuthAuthorizationServerProvider>()

                .CreatesOncePerRequest<SystemStatusController>()
                .CreatesOncePerRequest<ErrorsController>()
                .CreatesOncePerRequest<UpdatesController>()
                .CreatesOncePerApp<MultipartFormDataUploadProvider>()
                .CreatesOncePerApp<StaticFileSystemConfiguration>()
                .CreatesOncePerRequest<CommandWebApiController>()
                .CreatesOncePerRequest<AnonymousCommandController>()
                .CreatesOncePerApp<ResolverCommandFactory>()
                .Includes<AppStatusServerModule>() //server modules always after client module
                .Includes<DDDServerModule>()
                //so adapters can be swapped out
                 ;


        }
    }
}