using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using Rxns.DDD.Commanding;
using Rxns.Interfaces;

namespace Rxns.Hosting
{
    /// <summary>
    /// Creates reactions after the container has been built
    /// </summary>
    public class PostBuildRxnServiceCreator : IContainerPostBuildService
    {
        public void Run(IReportStatus logger, IResolveTypes container)
        {
            foreach (var service in container.Resolve<IEnumerable<IRxnService>>())
                try
                {
                    logger.OnInformation("Starting service: {0}", service.GetType());
                    service.Start()
                        .Timeout(TimeSpan.FromMinutes(5))
                        .Catch<CommandResult, TimeoutException>(_ => { throw new Exception("Timed out while starting {0}".FormatWith(service.GetType().Name)); })
                        .Until(logger.OnError);
                }
                catch (Exception e)
                {
                    logger.OnError("Service: {0} failed to startup: {1}", service.GetType(), e);
                }
        }
    }
}

