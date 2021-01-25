using System;
using System.Reactive;
using Rxns.CQRS;
using Rxns.DDD.Commanding;
using Rxns.DDD.CQRS;
using Rxns.Hosting;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns.AppHost
{

    public class OutOfProcessCache : IRxnProcessor<StartReactor>, IDomainQueryHandler<LookupReactorCountQry, int>, IRxnCfg, IRxnProcessor<LookupReactorCount>, IDomainCommandHandler<IncrementReactorCount, bool>
    {
        private int processCount = 0;

        public OutOfProcessCache()
        {
            "Starting cache".LogDebug();
        }

        public IObservable<IRxn> Process(StartReactor @event)
        {
            processCount++;
            return Rxn.Empty();
        }

        public IObservable<int> Handle(LookupReactorCountQry message)
        {
            return processCount.ToObservable();
        }

        public string Reactor => "Cache";
        public IObservable<IRxn> ConfigureInput(IObservable<IRxn> pipeline)
        {
            return pipeline;
        }

        public IDeliveryScheme<IRxn> InputDeliveryScheme { get; }
        public bool MonitorHealth { get; }
        public RxnMode Mode => RxnMode.OutOfProcess;


        public IObservable<IRxn> Process(LookupReactorCount command)
        {
            return CommandResult.Success(processCount.ToString()).AsResultOf(command).ToObservable();
        }

        public IObservable<DomainCommandResult<bool>> Handle(IncrementReactorCount message)
        {
            processCount++;
            return DomainCommandResult<bool>.FromSuccessfulResult(message.Id, true).ToObservable();
        }
    }

    public class IncrementReactorCount : TenantCmd<bool>
    {
    }

    public static class OutOfProcessDemo
    {
        public static Func<Action<IRxnLifecycle>, Action<IRxnLifecycle>> DemoApp = d =>
        {
            return dd =>
            {
                d(dd);    
                dd.CreatesOncePerApp<OutOfProcessCache>();
                dd.RespondsToSvcCmds<LookupReactorCount>();
                dd.RespondsToSvcCmds<LookupReactorCountQry>();
                dd.RespondsToSvcCmds<IncrementReactorCount>();
                dd.RespondsToCmd<IncrementReactorCount>();
                dd.RespondsToQry<LookupReactorCountQry>();
                
            };
        };
    }
}
