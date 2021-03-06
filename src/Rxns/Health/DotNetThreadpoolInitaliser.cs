﻿using System;
using System.Reactive;
using Rxns.Interfaces;

namespace Rxns.Health
{
    /// <summary>
    /// This services sets the RxnSchedulers.TaskPool on startup to the configured
    /// value in ISystemResourceConfiguration. 
    /// </summary>
    public class DotNetThreadPoolThresdholdInitialiser : IContainerPostBuildService
    {
        private readonly ISystemResourceConfiguration _configuration;

        public DotNetThreadPoolThresdholdInitialiser(ISystemResourceConfiguration configuration)
        {
            _configuration = configuration;
        }

        public IObservable<Unit> Run(IReportStatus logger, IResolveTypes container)
        {
            return Rxn.Create(() =>
            {
                var poolSize = _configuration.ThreadPoolSize > 0 ? _configuration.ThreadPoolSize : 8;
                logger.OnVerbose("Configuring RxnSchedulers.TaskPool thread pool size to: {0}", poolSize);

                RxnSchedulers.ThreadPoolSize = poolSize;
            });
        }
    }
}
