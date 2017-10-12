using System;
using Rxns.Interfaces;

namespace Rxns
{
    /// <summary>
    /// A performance oriendtated delivery scheme which delivers a message
    /// without garentee the message has been processed / without error
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class NoGaretenee<T> : IDeliveryScheme<T>
    {
        public void Deliver(T @event, Action<T> postBox)
        {
            postBox(@event);
        }

        public IObservable<T> Deliver(T @event, Func<T, IObservable<T>> postBox)
        {
            return postBox(@event);
        }
    }
}
