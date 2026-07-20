using System;
using System.Reactive;
using System.Reactive.Linq;

namespace Rxns.DDD.Commanding
{
    /// <summary>
    /// Helper class for asynchronous requests that return a void response
    /// </summary>
    /// <typeparam name="TMessage">The type of void request being handled</typeparam>
    public abstract class AsyncRequestHandler<TMessage> : IRxnMediatorPipeline<TMessage, Unit>
        where TMessage : IAsyncRequest
    {
        public IObservable<Unit> Handle(TMessage message)
        {
            return Rxn.Create(() => HandleCore(message));
        }

        /// <summary>
        /// Handles a void request
        /// </summary>
        /// <param name="message">The request message</param>
        /// <returns>A task representing the void response from the request</returns>
        protected abstract IObservable<Unit> HandleCore(TMessage message);
    }
}
