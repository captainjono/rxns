using System;
using System.Reactive;
using System.Reactive.Linq;
using Rxns.Hosting.Updates;
using Rxns.Interfaces;
using Rxns.Microservices;

namespace Rxns.Health.AppStatus
{
    public class GetOrCreateAppStatusStoreStartupTask : IContainerPostBuildService
    {
        public IObservable<Unit> Run(IReportStatus logger, IResolveTypes container)
        {
            return Rxn.Create(() =>
            {
                //The store can be via via the rest-api
                var appStatusStore = container.Resolve<IAppStatusStore>();
                var appLogs = container.Resolve<IAppContainer>();
                appLogs.SubscribeAll(appStatusStore.Add, appStatusStore.Add);
            });
        }
    }
}
