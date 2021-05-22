using System;
using Rxns.DDD.Commanding;
using Rxns.DDD.CQRS;

namespace Rxns.DDD.BoundedContext
{
    public interface IDomainEvent : IUniqueRxn, IRequireTenantContext
    {
        DateTime Timestamp { get; }
    }
}
