using System;

namespace Rxns.DDD.Commanding
{
    internal abstract class AsyncRequestHandlerWrapper<TResult>
    {
        public abstract IObservable<TResult> Handle(IAsyncRequest<TResult> message);
    }

    internal class AsyncRequestHandlerWrapper<TCommand, TResult> : AsyncRequestHandlerWrapper<TResult>
        where TCommand : IAsyncRequest<TResult>
    {
        private readonly IRxnMediatorPipeline<TCommand, TResult> _inner;

        public AsyncRequestHandlerWrapper(IRxnMediatorPipeline<TCommand, TResult> inner)
        {
            _inner = inner;
        }

        public override IObservable<TResult> Handle(IAsyncRequest<TResult> message)
        {
            return _inner.Handle((TCommand)message);
        }
    }
}
