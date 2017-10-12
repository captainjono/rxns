using System;
using System.Reactive;
using System.Reactive.Subjects;
using Rxns.Logging;
using Rxns.System.Collections.Generic;

namespace Rxns
{
    /// <summary>
    /// An class which aguments another type, adding an event sourcing flavour
    /// to its behaviour.
    /// 
    /// "in reaction to this event, do this operation on the decorated class"
    /// </summary>
    /// <typeparam name="TEvents"></typeparam>
    /// <typeparam name="TDecorated"></typeparam>
    public class RxnDecorator<TEvents, TDecorated> : ReportsStatus, IObservable<TEvents>
    {
        private readonly Action<TEvents, TDecorated>[] _transformations;
        private readonly TDecorated _decorated;
        private readonly Subject<Unit> _onUpdated;
        private readonly IObservable<TEvents> _events;
        
        /// <summary>
        /// Occours when the a transformer has applied a change to the instance
        /// </summary>
        public IObservable<Unit> OnUpdated { get { return _onUpdated; } }

        /// <summary>
        /// Decorates a type with event sourcing sauce 
        /// </summary>
        /// <param name="decorated">An instance of the type to decorate</param>
        /// <param name="events">The stream that drives the transformers</param>
        /// <param name="transformations">A series of transformers, that mutate the decorated instance based on a specific rxn</param>
        public RxnDecorator(TDecorated decorated, IObservable<TEvents> @events, params Action<TEvents, TDecorated>[] transformations)
        {
            _onUpdated = new Subject<Unit>().DisposedBy(this);
            _decorated = decorated;
            _transformations = transformations;
            _events = @events;

            @events.Subscribe(this, DoOperationForEvent).DisposedBy(this);
        }

        private void DoOperationForEvent(TEvents e)
        {
            _transformations.ForEach(t => t(e, _decorated));
        }

        /// <summary>
        /// Subscribes to the raw rxn stream used to power this decorator
        /// </summary>
        /// <param name="observer"></param>
        /// <returns></returns>
        public IDisposable Subscribe(IObserver<TEvents> observer)
        {
            return _events.Subscribe(observer);
        }
    }
}
