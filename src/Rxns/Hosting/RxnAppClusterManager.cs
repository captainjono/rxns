using System;
using System.Collections.Generic;
using System.Text;
using Rxns.Cloud;
using Rxns.Interfaces;

namespace Rxns.Hosting
{
    public class RxnAppClusterManager : IRxnProcessor<AppProcessStarted>, IRxnPublisher<IRxn>
    {
        private int appCount = 0;
        public Func<object> ClusterStatus;

        public RxnAppClusterManager()
        {
            ClusterStatus = () => new
            {
                Processes = appCount,
                Cluster = "Healthy"
            };
        }

        public IObservable<IRxn> Process(AppProcessStarted @event)
        {
            appCount++;
            return Rxn.Empty();
        }

        public IObservable<IRxn> Process(AppProcessEnded @event)
        {
            appCount--;
            return Rxn.Empty();
        }

        public void ConfigiurePublishFunc(Action<IRxn> publish)
        {
            publish(new AppStatusInfoProviderEvent()
            {
                Info = ClusterStatus
            });
        }
    }
}
