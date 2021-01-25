using System.Reactive.Linq;
using Rxns.Hosting.Updates;
using Rxns.Interfaces;
using Rxns.Microservices;

namespace Rxns.WebApi.AppStatus
{
    public class GetOrCreateAppStatusStoreStartupTask : IContainerPostBuildService
    {
        public void Run(IReportStatus logger, IResolveTypes container)
        {
            //The store can be via via the rest-api
            var appStatusStore = container.Resolve<IAppStatusStore>();
            var appLogs = container.Resolve<IAppContainer>();
            appLogs.SubscribeAll(appStatusStore.Add, appStatusStore.Add);
        }
    }
}
