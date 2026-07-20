using Rxns.DDD.Commanding;

namespace Rxns.DDD.CQRS
{
    public interface IDomainQuery : IServiceCommand
    {
        
    }

    public interface IDomainQuery<out T> : IAsyncRequest<T>, IDomainQuery
    {
    }
}
