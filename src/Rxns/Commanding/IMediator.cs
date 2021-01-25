using System;

namespace Rxns.DDD.Commanding
{
    /// <summary>
    /// An object which mediators the execution of something, decoupling the corcerns to a request and response scenario
    /// </summary>
    public interface IMediator
    {
        IObservable<TResponse> SendAsync<TResponse>(IAsyncRequest<TResponse> request);

        Type Handles();
    }
}
