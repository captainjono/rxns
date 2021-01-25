using System;
using System.Reactive.Subjects;
using Rxns.Health;
using Rxns.Interfaces;

namespace Rxns.DDD.CQRS
{
    public class AppHealthManager : IRxnHealthManager, IReportHealth, IRxnCfg, IRxnPublisher<IRxn>
    {
        public string ReporterName => "DomainCmds";
        public ISubject<IHealthEvent> Pulse { get; private set; }
        public void Shock()
        {

        }

        public string Reactor { get; }
        public IObservable<IRxn> ConfigureInput(IObservable<IRxn> pipeline)
        {
            return pipeline;
        }

        public IDeliveryScheme<IRxn> InputDeliveryScheme { get; }
        public bool MonitorHealth => false;
        public RxnMode Mode { get; }

        public void ConfigiurePublishFunc(Action<IRxn> publish)
        {
            //currently only implementing this to get "health status".
            //should create an activator for non-service rxns to be hooked up on resolution
        }

        public AppHealthManager()
        {
            Pulse = new Subject<IHealthEvent>();
        }
        public void Publish(IHealthEvent pulse)
        {
            Pulse.OnNext(pulse);
        }

    }
}
