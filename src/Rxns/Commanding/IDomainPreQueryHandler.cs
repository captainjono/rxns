using Rxns.CQRS;

namespace Rxns.DDD.Commanding
{
    public interface IPreRequestHandler<in TRequest>
    {
        void Handle(TRequest message);
    }

    /// <summary>
    /// A handler which is executed before the target commandHandler is run
    /// </summary>
    /// <typeparam name="TRequest"></typeparam>
    public interface IDomainQueryPreHandler<in TRequest> : IPreRequestHandler<TRequest>
    {
    }

    public interface IDomainCommandPreHandler<in TRequest> : IPreRequestHandler<TRequest>
    {
    }

    public interface IDomainQueryPostHandler<in TRequest, TResponse> : IPostRequestHandler<TRequest, TResponse>
    {
        void Handle(TRequest message, TResponse response);

    }

    public interface IDomainCommandPostHandler<in TRequest, TResponse> : IPostRequestHandler<TRequest, IDomainCommandResult<TResponse>>
    {
    }
}
