using System;
using System.Linq;
using System.Reactive.Linq;

namespace Rxns.Commanding
{
    public class AsyncMediatorPipeline<TRequest, TResponse>
         : IAsyncRequestHandler<TRequest, TResponse>
         where TRequest : IAsyncRequest<TResponse>
    {

        private readonly IAsyncRequestHandler<TRequest, TResponse> _inner;
        private readonly IPreRequestHandler<TRequest>[] _preRequestHandlers;
        private readonly IPostRequestHandler<TRequest, TResponse>[] _postRequestHandlers;

        public AsyncMediatorPipeline(
            IAsyncRequestHandler<TRequest, TResponse> inner,
            IPreRequestHandler<TRequest>[] preRequestHandlers,
            IPostRequestHandler<TRequest, TResponse>[] postRequestHandlers
            )
        {
            _inner = inner;
            _preRequestHandlers = preRequestHandlers;
            _postRequestHandlers = postRequestHandlers;
        }

        public IObservable<TResponse> Handle(TRequest message)
        {
            return Observable.Create<TResponse>(o =>
            {
                foreach (var preRequestHandler in _preRequestHandlers.Where(w => w != null))
                {
                    preRequestHandler.Handle(message);
                }

                return _inner.Handle(message)
                            .Select(result =>
                            {
                                foreach (var postRequestHandler in _postRequestHandlers.Where(w => w != null))
                                {
                                    postRequestHandler.Handle(message, result);
                                }
                                return result;
                            })
                            .Subscribe(o);
            });
        }
    }
}
