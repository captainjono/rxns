using System;

namespace RxnsDemo.AzureB2C.Rxns
{
    public interface IExecutionContext
    {
        Lazy<ITenantContext> Tenant { get; }
        Lazy<IUserContext> User { get; }
    }
}
