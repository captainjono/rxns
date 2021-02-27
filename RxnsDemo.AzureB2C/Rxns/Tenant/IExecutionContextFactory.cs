using Rxns.CQRS;

namespace RxnsDemo.AzureB2C.Rxns
{
    public interface IExecutionContextFactory
    {
        IExecutionContext FromDomain(IRequireTenantContext @event);
        IExecutionContext FromUserDomain(IRequireUserContext @event);
        IExecutionContext FromUser(string tenant, string user);
        IExecutionContext FromTenant(string tenant);
    }
}
