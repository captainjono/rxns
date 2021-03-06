﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
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
        public IObservable<Unit> Run(IReportStatus logger, IResolveTypes container)
        {
            return container.Resolve<IEnumerable<IRxnService>>().Reverse().ToObservable().SelectMany(service =>
            {
                try
                {
                    logger.OnInformation("Starting service: {0}", service.GetType());
                    return service.Start()
                        .Timeout(TimeSpan.FromMinutes(5))
                        .Catch<CommandResult, TimeoutException>(_ =>
                        {
                            throw new Exception("Timed out while starting {0}".FormatWith(service.GetType().Name));
                        })
                        ;
                }
                catch (Exception e)
                {
                    logger.OnError(new Exception("Service: {0} failed to startup: {1}".FormatWith(service.GetType()), e));

                    return CommandResult.Failure(e.Message).ToObservable();
                }
            })
            .LastOrDefaultAsync()
            .Select(_ => new Unit())
            ;
        }
    }
}

