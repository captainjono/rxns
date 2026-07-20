using System;

namespace Rxns.DDD.Tenant
{
    public interface IExecutionContext
    {
        Lazy<ITenantContext> Tenant { get; }
        Lazy<IUserContext> User { get; }
    }
}
