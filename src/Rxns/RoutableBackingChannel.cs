using System;
using System.Collections.Generic;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Rxns.Interfaces;
using Rxns.Logging;


namespace Rxns
{
    public class LocalOnlyRegistry : IRxnManagerRegistry
    {
        public LocalOnlyRegistry(IRxnManager<IRxn> all)
        {
            RxnsCentral = all;
            RxnsCentral = all;
            RxnsCentralReliable = all;
        }
        public IRxnManager<IRxn> RxnsLocal { get; }
        public IRxnManager<IRxn> RxnsCentralReliable { get; }
        public IRxnManager<IRxn> RxnsCentral { get; }
        public IDictionary<string, IRxnManager<IRxn>> ClientRoutes { get; }
    }


    /// <summary>
    /// A backing channel which forwards events to other backing channels based
    /// on a routing configuration. Each event is delivered to at most one channel,
    /// and if no route exists, will be delivered on the default cahnnel
    ///
    /// repeating backing channel setup
    /// var routingTable = new RoutableBackingChannel<IEvent>(registry);
    /// var rb = new RxnRouteCfg();
    /// routingTable.Configure(_eventsToRepeat.Select(type => rb.OnReactionTo(type).PublishTo(registry.EventsCentral).AndTo(registry.EventsLocal)).Concat(new [] { rb.OnReaction().PublishTo(registry.EventsLocal) }).ToArray());
    /// </summary>
    /// <typeparam name="T">The base type of rxn the backing channel routes</typeparam>
    public class RoutableBackingChannel<T> : IRxnBackingChannel<T>
    {
        private readonly IRxnManagerRegistry _services;
        private readonly IScheduler _routingScheduler;
        private readonly Subject<T> _routingPipeline = new Subject<T>();
        private IDisposable _setupResource;

        public IDictionary<string, IRxnRouteCfg<T>> Routes { get; private set; }

        /// <summary>
        /// Creates a new backin channel with a registery which is used to locate the appropriote channel
        /// for each route. 
        /// </summary>
        /// <param name="services">The route table</param>
        /// <param name="routingScheduler">The schedule used to perform routing</param>
        public RoutableBackingChannel(IRxnManagerRegistry registey, IScheduler routingScheduler = null)
        {
            _services = registey;
            _routingScheduler = routingScheduler;
            Routes = new Dictionary<string, IRxnRouteCfg<T>>();
        }
        
        public IDisposable ConfigureWith(string name, IRxnRouteCfg<T> cfg)
        {
            Routes.Add(name, cfg);

            return new DisposableAction(() =>
            {
                Forget(name);
            });
        }

        private void Forget(string route)
        {
            if(Routes.ContainsKey(route))
                Routes.Remove(route);
        }

        private void RouteEvent(T @event)
        {
            var called = false;

            foreach (var route in Routes.Values)
                foreach (var condition in route.Conditions)
                    if (condition(@event))
                    {
                        route.Destinations.ForEach(d => d(@event));//Until());
                        called = true;
                    }

            if(called) return;

            ReportStatus.Log.OnWarning("No route for {0}", @event.GetType());
        }
        /// <summary>
        /// The scheme provided to this method is not respected as the this
        /// backing channel is just a router that forwards requests to other eventManagers
        /// which already have schemes defined for them.
        /// 
        /// The returned channel is a conduit to the local rxn manager defined in the systemRegistry
        /// </summary>
        /// <param name="scheme"></param>
        /// <returns></returns>
        public IObservable<T> Setup(IDeliveryScheme<T> scheme = null)
        {
            IObservable<T> received = _routingPipeline;
            //only setup the pipeline on the first subscription
            if (_setupResource == null)
            {
                if (_routingScheduler != null) received = received.ObserveOn(_routingScheduler);
                _setupResource = received.Do(RouteEvent).Until(ReportStatus.Log.OnError);
            }

            return _services.RxnsLocal.CreateSubscription<T>().FinallyR(() =>
            {
                _setupResource.Dispose();
                _setupResource = null;
            });
        }
        
        public void Publish(T message)
        {
            _routingPipeline.OnNext(message);
        }
    }
}
