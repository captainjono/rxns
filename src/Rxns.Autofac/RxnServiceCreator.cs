using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using Autofac;
using Rxns.Commanding;
using Rxns.Interfaces;

namespace Rxns.Autofac
{
    /// <summary>
    /// This is a container build service which handles types that implement IRedViweService
    /// and starts them up when the container is built
    /// </summary>
    public class RxnsServiceCreator : IContainerPostBuildService
    {
        public void Run(IReportStatus logger, IContainer container)
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

