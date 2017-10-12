using System;

namespace Rxns.Interfaces
{
    /// <summary>
    /// A reactive rxn bus which implements a "hot" stream
    /// </summary>
    /// <typeparam name="T">The type of message the rxn bus handles. Using a base-type here is a good idea</typeparam>
    public interface IRxnManager<T> : IDisposable
    {
        /// <summary>
        /// Sets the rxn bus up  ready to route messages
        /// </summary>
        void Activate();
        /// <summary>
        /// Creates a subscription to the rxn bus that will receive all messages sent over it
        /// </summary>
        /// <returns></returns>
        IObservable<T> CreateSubscription();
        /// <summary>
        /// Creates a subscription to the rxn bus that receieved a subset of the messages sent over it,
        /// that can be assigned to TMessageType.
        /// </summary>
        /// <typeparam name="TMessageType">The base type to receive messages for</typeparam>
        /// <returns></returns>
        IObservable<TMessageType> CreateSubscription<TMessageType>();
        /// <summary>
        /// Publishes an rxn to the rxn bus. Successfully running this method indicates
        /// a success publish, but depending on the backing channel, may not be received
        /// on a subscription until sometime later
        /// </summary>
        /// <param name="message"></param>
        void Publish(T message);
    }
}

    