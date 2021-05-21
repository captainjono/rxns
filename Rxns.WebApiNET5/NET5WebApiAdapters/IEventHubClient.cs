using System;
using System.Reactive;
using Rxns.Interfaces;

namespace Rxns.WebApiNET5.NET5WebApiAdapters
{
    /// <summary>
    /// This client is used to connect to an event hub to facilitate remoting of
    /// events
    /// </summary>
    public interface IEventHubClient: IDisposable
    {
        string Url { get; set; }
        /// <summary>
        /// Publishes an event to the event hub
        /// </summary>
        /// <param name="message"></param>
        void Publish(IRxn message);
        /// <summary>
        /// Subscribes to the event hub for remote events
        /// </summary>
        /// <returns></returns>
        IObservable<IRxn> CreateSubscription();

        IObservable<Unit> Connect();
    }
}
