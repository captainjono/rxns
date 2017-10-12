using System;

namespace Rxns.Interfaces
{
    /// <summary>
    /// Defines a service which delivers an rxn to postbox. The action of delivering
    /// an rxn (input) can result in another rxn being generated (output sideffect)
    /// </summary>
    /// <typeparam name="TEvent"></typeparam>
    public interface IDeliveryScheme<TEvent>
    {
        /// <summary>
        /// Used to complete a sync delivery of an rxn to a postbox
        /// </summary>
        /// <param name="event">The rxn to deliver</param>
        /// <param name="postBox">The action that delivers the rxn</param>
        void Deliver(TEvent @event, Action<TEvent> postBox);
        /// <summary>
        /// used to deliver a stream of events to a postbox, which may return
        /// an rxn as its result
        /// </summary>
        /// <param name="event">The rxn stream</param>
        /// <param name="postBox">The function which delivers the evnet and returns any sideeffects</param>
        /// <returns></returns>
        IObservable<TEvent> Deliver(TEvent @event, Func<TEvent, IObservable<TEvent>> postBox);
    }
}
