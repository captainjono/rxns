using System;
using System.Reactive.Concurrency;
using Rxns.Interfaces;
using Rxns.Microservices;

namespace Rxns.Hosting
{
    public interface IRxnAppFactory
    {
        IRxnAppContext Create<T>(IReportStatus logger, IAppContainer cfg, IScheduler scheduler) where T : IRxnApp;
        IRxnAppContext Create(Type rxnApp, IReportStatus logger, IAppContainer cfg, IScheduler eventDelivery);
        IRxnAppContext Create(IRxnApp rxnApp, IReportStatus logger, IAppContainer cfg, IScheduler eventDelivery);
    }
}
