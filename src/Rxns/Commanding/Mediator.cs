using System;

namespace Rxns.Commanding
{

    /// <summary>
    /// This code has been borrowed heavily from the mediatoR project;
    /// https://github.com/jbogard/MediatR/
    /// 
    /// Default mediator implementation relying on single- and multi instance delegates for resolving handlers.
    /// </summary>
    public abstract class Mediator : IMediator
    {
        private readonly SingleInstanceFactory _singleInstanceFactory;

        /// <summary>
        /// Initializes a new instance of the <see cref="Mediator"/> class.
        /// </summary>
        /// <param name="singleInstanceFactory">The single instance factory.</param>
        public Mediator(SingleInstanceFactory singleInstanceFactory)//, MultiInstanceFactory multiInstanceFactory)
        {
            _singleInstanceFactory = singleInstanceFactory;
        }
        
        public IObservable<TResponse> SendAsync<TResponse>(IAsyncRequest<TResponse> request)
        {
            var defaultHandler = GetHandler(request);

            return defaultHandler.Handle(request);
        }

        private AsyncRequestHandlerWrapper<TResponse> GetHandler<TResponse>(IAsyncRequest<TResponse> request)
        {
            return GetHandler<AsyncRequestHandlerWrapper<TResponse>, TResponse>(request,
                typeof(IAsyncRequestHandler<,>),
                typeof(AsyncRequestHandlerWrapper<,>));
        }

        private TWrapper GetHandler<TWrapper, TResponse>(object request, Type handlerType, Type wrapperType)
        {
            var requestType = request.GetType();
                
            var genericHandlerType = handlerType.MakeGenericType(requestType, typeof(TResponse));
            var genericWrapperType = wrapperType.MakeGenericType(requestType, typeof(TResponse));

            var handler = GetHandler(request, genericHandlerType);

            return (TWrapper) Activator.CreateInstance(genericWrapperType, handler);
        }

        private object GetHandler(object request, Type handlerType)
        {
            try
            {
                return _singleInstanceFactory(handlerType);
            }
            catch (Exception e)
            {
                throw BuildException(request, e);
            }
        }
        
        private static InvalidOperationException BuildException(object message, Exception inner)
        {
            return new InvalidOperationException("Handler was not found for request of type " + message.GetType() + ".\r\nContainer or service locator not configured properly or handlers not registered with your container.", inner);
        }

        /// <summary>
        /// The type derrived from IAsyncCommandHandler; used to allow easier component to mediator linking in IoC
        /// </summary>
        /// <returns></returns>
        public abstract Type Handles();
    }
}

