using System;
using Autofac;
using Autofac.Features.OwnedInstances;
using Microsoft.AspNet.SignalR.Client;
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
                .CreatesOncePerApp<AspnetCoreOnReadyHandler>()

                //.CreatesOncePerApp<HostBuffer>()


                //.CreatesOncePerAppAs<NoOAuthAuthentication, OAuthAuthorizationServerProvider>()

                .CreatesOncePerRequest<SystemStatusController>()
                .CreatesOncePerRequest<ErrorsController>()
                .CreatesOncePerRequest<UpdatesController>()
                .CreatesOncePerApp<MultipartFormDataUploadProvider>()
                .CreatesOncePerApp<StaticFileSystemConfiguration>()
                .CreatesOncePerRequest<CommandWebApiController>()
                .CreatesOncePerRequest<AnonymousCommandController>()
                //.CreatesOncePerApp<AspnetCoreControllerLinkProvider>()
                .CreatesOncePerApp<ResolverCommandFactory>()
                .Includes<AppStatusServerModule>() //server modules always after client module
                .Includes<DDDServerModule>()

            //this is a connection factory takes a url and returns a signalR client
            .CreatesOncePerApp<Func<string, Owned<HubConnection>>>(c =>
            {
                var cc = c.Resolve<IComponentContext>();
                return (url) =>
                {
                    var lifetime = cc.Resolve<ILifetimeScope>().BeginLifetimeScope();
                    return new Owned<HubConnection>(new HubConnection(url), lifetime);
                };
            });

            //so adapters can be swapped out
            ;


        }
    }
}