using System;

namespace Rxns.Commanding
{
    internal abstract class AsyncRequestHandlerWrapper<TResult>
    {
        public abstract IObservable<TResult> Handle(IAsyncRequest<TResult> message);
    }

    internal class AsyncRequestHandlerWrapper<TCommand, TResult> : AsyncRequestHandlerWrapper<TResult>
        where TCommand : IAsyncRequest<TResult>
    {
        private readonly IAsyncRequestHandler<TCommand, TResult> _inner;

        public AsyncRequestHandlerWrapper(IAsyncRequestHandler<TCommand, TResult> inner)
        {
            _inner = inner;
        }

        public override IObservable<TResult> Handle(IAsyncRequest<TResult> message)
        {
            return _inner.Handle((TCommand)message);
        }
    }
}
