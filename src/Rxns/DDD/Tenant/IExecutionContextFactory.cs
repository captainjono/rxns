using Rxns.DDD.CQRS;

namespace Rxns.DDD.Tenant
{
    public interface IExecutionContextFactory
    {
        IExecutionContext FromDomain(IRequireTenantContext @event);
        IExecutionContext FromUserDomain(IRequireUserContext @event);
        IExecutionContext FromUser(string tenant, string user);
        IExecutionContext FromTenant(string tenant);
    }
}
