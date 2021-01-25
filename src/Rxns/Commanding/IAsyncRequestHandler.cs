﻿using System;

namespace Rxns.DDD.Commanding
{
    /// <summary>
    /// Defines an asynchronous handler for a request
    /// </summary>
    /// <typeparam name="TRequest">The type of request being handled</typeparam>
    /// <typeparam name="TResponse">The type of response from the handler</typeparam>
    public interface IRxnMediatorPipeline<in TRequest, TResponse> : IAsyncRequestHandler<TRequest, TResponse>
        where TRequest : IAsyncRequest<TResponse>
    {
        /// <summary>
        /// Handles an asynchronous request
        /// </summary>
        /// <param name="message">The request message</param>
        /// <returns>A task representing the response from the request</returns>
        IObservable<TResponse> Handle(TRequest message);
    }
}
