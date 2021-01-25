using Microsoft.Owin.Security.OAuth;
using Rxns.Hosting;
using Rxns.Hosting.Updates;
using Rxns.Metrics;
using Rxns.NewtonsoftJson;
using Rxns.WebApi.AppStatus;
using Rxns.WebApi.AppStatus.Server;
using Rxns.WebApi.MsWebApiAdapters.RxnsApiAdapters;
using Rxns.WebApi.MsWebApiFeatures;

namespace Rxns.WebApi.OwinWebApiAdapters
{
    public class OwinWebApiAdapterModule : IAppModule
    {
        public IRxnLifecycle Load(IRxnLifecycle lifecycle)
        {
            
            return lifecycle
                .CreatesOncePerApp<ReportHub>()
                .CreatesOncePerApp<SystemMetricsHub>()
                .CreatesOncePerApp<EventsHub>()
                .CreatesOncePerApp<SystemMetricsReport>()
                .CreatesOncePerApp<MsWebApiRxnExceptionLogger>()
                .CreatesOncePerApp<HostBuffer>()

                .CreatesOncePerAppAs<NoOAuthAuthentication, OAuthAuthorizationServerProvider>()

                .CreatesOncePerRequest<SystemStatusController>()
                .CreatesOncePerRequest<ErrorsController>()
                .CreatesOncePerRequest<UpdatesController>()
                .CreatesOncePerApp<MultipartFormDataUploadProvider>()

                .CreatesOncePerRequest<CommandWebApiController>()
                .CreatesOncePerRequest<AnonymousCommandController>()
                .CreatesOncePerApp<ResolverCommandFactory>();


        }
    }
}
