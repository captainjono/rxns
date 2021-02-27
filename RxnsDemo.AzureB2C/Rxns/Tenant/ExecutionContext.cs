using System;

namespace RxnsDemo.AzureB2C.Rxns
{
    public class ExecutionContext : IExecutionContext
    {
        public Lazy<ITenantContext> Tenant { get; private set; }
        public Lazy<IUserContext> User { get; private set; }

        public ExecutionContext(string tenant, string userName, ITenantContextFactory contextFactory)
        {
            Tenant = new Lazy<ITenantContext>(() => contextFactory.GetContext(tenant));
            User = new Lazy<IUserContext>(() => contextFactory.GetUserContext(tenant, userName));
        }

        /// <summary>
        /// Creates an execution context using the specified tenant and the current threads IPrinciple for the user
        /// </summary>
        /// <param name="tenant"></param>
        /// <param name="contextFactory"></param>
        public ExecutionContext(string tenant, ITenantContextFactory contextFactory)
        {
            Tenant = new Lazy<ITenantContext>(() => contextFactory.GetContext(tenant));
            User = new Lazy<IUserContext>(() => contextFactory.GetUserContext(tenant));
        }
    }
}
