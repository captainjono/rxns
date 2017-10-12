using System;

namespace Rxns.Interfaces
{
    /// <summary>
    /// A backing channel is a one way message pipeline that sends each rxn
    /// published to the stream that it sets up
    /// </summary>
    /// <typeparam name="T">The type of data the backing channel will tunnel</typeparam>
    public interface IRxnBackingChannel<T>
    {
        /// <summary>
        /// Sets the backing channel up ready to publish messages.
        /// </summary>
        /// <returns>An observable channel to view the data published 
        /// note: this does not include historical data, only new data</returns>
        IObservable<T> Setup(IDeliveryScheme<T> postman);
        /// <summary>
        /// Publishes a message across the channel
        /// </summary>
        /// <param name="message">The message to publish</param>
        void Publish(T message);
    }
}
