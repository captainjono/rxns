using System;

namespace RxnsDemo.AzureB2C.Rxns.Tenant
{
    public interface IExecutionContext
    {
        Lazy<ITenantContext> Tenant { get; }
        Lazy<IUserContext> User { get; }
    }
}
