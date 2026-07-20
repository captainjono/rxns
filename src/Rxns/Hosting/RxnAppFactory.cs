using System;
using System.Reactive.Concurrency;
using Rxns.Interfaces;
using Rxns.Microservices;

namespace Rxns.Hosting
{
    public class RxnAppFactory : IRxnAppFactory
    {
        public IRxnAppContext Create<T>(IReportStatus logger, IAppContainer cfg, IScheduler eventDelivery = null)
            where T : IRxnApp
        {
            return Create(typeof(T), logger, cfg, eventDelivery);
        }

        public IRxnAppContext Create(Type rxnApp, IReportStatus logger, IAppContainer cfg, IScheduler eventDelivery = null)
        {
            return new InProcessRxnAppContext(cfg.Resolve(rxnApp) as IRxnApp, cfg);
        }

        //this should not return an appcontext.
        //appcontext should only be for started apps?
        public IRxnAppContext Create(IRxnApp app, IReportStatus logger, IAppContainer cfg, IScheduler eventDelivery = null)
        {
            return new InProcessRxnAppContext(app, cfg);
        }
    }
}
