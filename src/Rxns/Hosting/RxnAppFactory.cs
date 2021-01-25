using System;
using System.Reactive.Concurrency;
using Rxns.Interfaces;

namespace Rxns.Hosting
{
    public class RxnAppFactory : IRxnAppFactory
    {
        public IRxnAppContext Create<T>(IReportStatus logger, IRxnDef cfg, IScheduler eventDelivery = null)
            where T : IRxnApp
        {
            return Create(typeof(T), logger, cfg, eventDelivery);
        }

        public IRxnAppContext Create(Type rxnApp, IReportStatus logger, IRxnDef cfg, IScheduler eventDelivery = null)
        {
            return new InProcessRxnAppContext(cfg.Container.Resolve(rxnApp) as IRxnApp, cfg.Container);
        }

        public IRxnAppContext Create(IMicroApp app, IReportStatus logger, IRxnDef cfg, IScheduler eventDelivery = null)
        {
            return new MicroAppContext(app, cfg.Container);
        }
    }
}
