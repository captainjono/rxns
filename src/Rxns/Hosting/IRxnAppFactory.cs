using System;
using System.Reactive.Concurrency;
using Rxns.Interfaces;

namespace Rxns.Hosting
{
    public interface IRxnAppFactory
    {
        IRxnAppContext Create<T>(IReportStatus logger, IRxnDef cfg, IScheduler scheduler) where T : IRxnApp;
        IRxnAppContext Create(Type rxnApp, IReportStatus logger, IRxnDef cfg, IScheduler eventDelivery);
        IRxnAppContext Create(IMicroApp rxnApp, IReportStatus logger, IRxnDef cfg, IScheduler eventDelivery);
    }
}
