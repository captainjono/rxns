using System;
using System.Linq;
using System.Reactive;
using System.Reflection;
using Rxns.Interfaces;

namespace Rxns.Hosting
{
    public class PostBuildRxnCreator : IContainerPostBuildService
    {
        public IObservable<Unit> Run(IReportStatus logger, IResolveTypes container)
        {
            return Rxn.Create(() =>
            {
                //here we look for all the IRxns registered, along with the interfaces they implement that also
                //can be considered IRxns.
                var ableToBeCreated = container.Resolve<IListKnownTypes>();
                var events = ableToBeCreated.Services.Where(s => typeof(IRxn).IsAssignableFrom(s) && !s.IsAbstract() && !s.GetTypeInfo().IsGenericTypeDefinition).ToArray();
                events = events.Concat(events.SelectMany(e => e.GetInterfaces().Where(ee => ee.IsAssignableTo<IRxn>()))).Distinct().ToArray();

                var resolver = container.Resolve<IResolveTypes>();

                //there is an ordering issue here, if i swap the next 2 the outofprocess reactors dont startup
                RxnCreator.StartRxnPublishers(logger, resolver, events);
                RxnCreator.StartRxnProcessors(logger, resolver, events, RxnSchedulers.TaskPool);
                RxnCreator.StartReactions(logger, resolver, new[] { typeof(IRxn) }, RxnSchedulers.TaskPool); //only supports IRxn atm
                var cfg = container.Resolve<IRxnFilterCfg>();

                var outOfProcess = container.Resolve<IRxnCfg[]>().Where(m =>
                {
                    return m.Mode == RxnMode.OutOfProcess && !m.Reactor.IsNullOrWhitespace() && !cfg.IsolateReactors.Contains(m.Reactor);
                }).Select(r => r.Reactor);

                var rxnManager = container.Resolve<IManageReactors>();

                foreach (var rxnCfg in outOfProcess)
                {
                    rxnManager.StartReactor(rxnCfg, RxnMode.OutOfProcess);
                }
            });
        }
    }
}