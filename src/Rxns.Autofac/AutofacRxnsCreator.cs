using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reflection;
using Autofac;
using Rxns.Interfaces;

namespace Rxns.Autofac
{
    public class AutofacRxnCreator : IContainerPostBuildService
    {
        public void Run(IReportStatus logger, IContainer container)
        {
            //here we look for all the IRxns registered, along with the interfaces they implement that also
            //can be considered IRxns.
            var events = container.ComponentRegistry.Registrations.Where(r => typeof(IRxn).IsAssignableFrom(r.Activator.LimitType) && !r.Activator.LimitType.IsAbstract() && !r.Activator.LimitType.GetTypeInfo().IsGenericTypeDefinition).Select(r => r.Activator.LimitType).ToArray();
            events = events.Concat(events.SelectMany(e => e.GetInterfaces().Where(ee => TypeExtensions.IsAssignableTo<IRxn>(ee)))).Distinct().ToArray();

            StartEventPublishers(logger, container, events);
            StartrxnProcessors(logger, container, events, RxnSchedulers.TaskPool);
            StartReactions(logger, container, new[] { typeof(IRxn) }, RxnSchedulers.TaskPool); //only supports IRxn atm
        }

        private void StartEventPublishers(IReportStatus logger, IContainer container, Type[] events)
        {
            var startedProcessors = new List<dynamic>();

            foreach (var @event in events)
            {
                try
                {
                    //now we create an rxnProcessor of each IRxn discovered, then ask the container
                    //for any interfaces which implement them.
                    var processorForEvent = typeof(IRxnPublisher<>).MakeGenericType(@event);
                    var allProcessorsForEventType = typeof(IEnumerable<>).MakeGenericType(processorForEvent);
                    var allProcessors = (IEnumerable<dynamic>)container.Resolve(allProcessorsForEventType);

                    Func<string, IReactor<IRxn>> getReactorByName = reactorName => container.Resolve<IManageReactors>().StartReactor(reactorName).Reactor;

                    var distinctProcessors = allProcessors.Distinct().ToArray();
                    //now register any processors found with the subscription to the events they are interested in
                    foreach (var publisher in distinctProcessors)
                    {
                        var reactor = RxnCreator.GetReactorFor(publisher as object, getReactorByName);
                        reactor.Connect(publisher); //this method will hookup ALL publisher methods ll
                    }
                    startedProcessors.AddRange(distinctProcessors); //so we need to keep a list processors we see so we dont attach them twice

                }
                catch (Exception e)
                {
                    logger.OnError(e);
                }
            }
        }

        private void StartReactions(IReportStatus logger, IContainer container, Type[] events, IScheduler eventDelivery)
        {
            foreach (var @event in events)
            {
                try
                {
                    var processorForEvent = typeof(IReactTo<>).MakeGenericType(@event);
                    var allProcessorsForEventType = typeof(IEnumerable<>).MakeGenericType(processorForEvent);
                    var allProcessors = (IEnumerable<dynamic>)container.Resolve(allProcessorsForEventType);

                    Func<string, Rxns.Interfaces.IReactor<IRxn>> getReactorByName = reactorName => container.Resolve<IManageReactors>().StartReactor(reactorName).Reactor;

                    //now attach any processors found with the subscription to the events they are interested in
                    foreach (object reaction in allProcessors.Distinct())
                    {
                        var reactor = RxnCreator.GetReactorFor(reaction, getReactorByName);
                        reactor.Connect((IReactTo<IRxn>)reaction, eventDelivery);
                    }
                }
                catch (Exception e)
                {
                    logger.OnError(e);
                }
            }
        }

        private void StartrxnProcessors(IReportStatus logger, IContainer container, Type[] events, IScheduler eventDelivery)
        {
            var startedProcessors = new List<dynamic>();

            foreach (var @event in events)
            {
                try
                {
                    //now we create an rxnProcessor of each IRxn discovered, then ask the container
                    //for any interfaces which implement them.
                    var processorForEvent = typeof(IRxnProcessor<>).MakeGenericType(@event);
                    var allProcessorsForEventType = typeof(IEnumerable<>).MakeGenericType(processorForEvent);
                    var allProcessors = ((IEnumerable<dynamic>)container.Resolve(allProcessorsForEventType)).ToArray();

                    Func<string, Rxns.Interfaces.IReactor<IRxn>> getReactorByName = reactorName => container.Resolve<IManageReactors>().StartReactor(reactorName).Reactor;

                    //now register any processors found with the subscription to the events they are interested in
                    var distinctProcessors = allProcessors.Distinct().ToArray();
                    foreach (var p in distinctProcessors.Except(startedProcessors))
                    {
                        var reactor = RxnCreator.GetReactorFor((object)p, getReactorByName);
                        //this method will hookup ALL process methods of a processor in one call
                        reactor.Connect((object)p, eventDelivery);
                    }

                    startedProcessors.AddRange(distinctProcessors); //so we need to keep a list processors we see so we dont attach them twice
                }
                catch (Exception e)
                {
                    logger.OnError(e);
                }
            }
        }
    }
}
