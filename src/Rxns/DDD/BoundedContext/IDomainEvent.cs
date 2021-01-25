using Rxns.CQRS;
using Rxns.DDD.Commanding;

namespace Rxns.DDD.BoundedContext
{
    public interface IDomainEvent : IUniqueRxn, IRequireTenantContext
    {

    }
}
