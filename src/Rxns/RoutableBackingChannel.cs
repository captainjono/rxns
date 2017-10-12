using System;
using System.Collections.Generic;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Rxns.Interfaces;
using Rxns.Logging;
using Rxns.System.Collections.Generic;

namespace Rxns
{
    /// <summary>
    /// A backing channel which forwards events to other backing channels based
    /// on a routing configuration. Each event is delivered to at most one channel,
    /// and if no route exists, will be delivered on the default cahnnel
    /// </summary>
    /// <typeparam name="T">The base type of rxn the backing channel routes</typeparam>
    public class RoutableBackingChannel<T> : ReportsStatus, IRxnBackingChannel<T>
    {
        private readonly IRxnManagerRegistry _services;
        private readonly IScheduler _routingScheduler;
        private readonly Subject<T> _routingPipeline = new Subject<T>();
        private IDisposable _setupResource;

        public List<IRxnRouteCfg<T>> Routes { get; private set; }

        /// <summary>
        /// Creates a new backin channel with a registery which is used to locate the appropriote channel
        /// for each route. 
        /// </summary>
        /// <param name="services">The route table</param>
        /// <param name="routingScheduler">The schedule used to perform routing</param>
        public RoutableBackingChannel(IRxnManagerRegistry services, IScheduler routingScheduler = null)
        {
            _services = services;
            _routingScheduler = routingScheduler;
            Routes = new List<IRxnRouteCfg<T>>();
        }

        private void RouteEvent(T @event)
        {
            foreach (var route in Routes)
                foreach (var condition in route.Conditions)
                    if (condition(@event))
                    {
                        route.Destinations.ForEach(d => d.Publish(@event));
                        return;
                    }

            OnWarning("No route for {0}", @event.GetType());
        }

        /// <summary>
        /// Configures the channel with a set of routes. Routes are applied in order
        /// of precedence, so route 0 will be evaluated before route 1, route 2, and so on.
        /// if many routes match, first in line, gets the cookie!
        /// </summary>
        /// <param name="route"></param>
        public void Configure(params IRxnRouteCfg<T>[] route)
        {
            Routes.AddRange(route);
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
                _setupResource = received.Do(RouteEvent).Subscribe(_ => { }, OnError);
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

        /// <summary>
        /// Destroys the backing channel ready to be setup again
        /// </summary>
        public override void Dispose()
        {
            if (_setupResource != null) _setupResource.Dispose();
            _setupResource = null;

            base.Dispose();
        }
    }
}
