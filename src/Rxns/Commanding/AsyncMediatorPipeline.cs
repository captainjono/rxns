using Rxns.DDD.CQRS;
using System;
using System.Linq;
using System.Reactive.Linq;
using Rxns.Health;

namespace Rxns.DDD.Commanding
{
    public class RxnMediatorPipeline<TRequest, TResponse>
         : IRxnMediatorPipeline<TRequest, TResponse>
         where TRequest : IAsyncRequest<TResponse>
    {
        private readonly IAsyncRequestHandler<TRequest, TResponse> _inner;
        private readonly IPreRequestHandler<TRequest>[] _preRequestHandlers;
        private readonly IPostRequestHandler<TRequest, TResponse>[] _postRequestHandlers;
        
        public RxnMediatorPipeline(
            IAsyncRequestHandler<TRequest, TResponse> inner,
            IPreRequestHandler<TRequest>[] preRequestHandlers,
            IPostRequestHandler<TRequest, TResponse>[] postRequestHandlers,
            IRxnHealthManager health  
            )
        {
            var metrics = new RequestElpasedTimeMonitor<TRequest, TResponse>($"RxnMediatorPipeline.{this.GetType().Name}", TimeSpan.FromSeconds(60), health);

            _inner = inner;
            _preRequestHandlers = new [] { metrics.StartTimer() }.Concat(preRequestHandlers).ToArray();
            _postRequestHandlers =  postRequestHandlers.Concat(new[] { metrics.EndTimer() }).ToArray();

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
