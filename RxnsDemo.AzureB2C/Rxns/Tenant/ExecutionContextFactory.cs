using Rxns.CQRS;

namespace RxnsDemo.AzureB2C.Rxns.Tenant
{
    /// <summary>
    /// A lightweight factory class that can create execution contexts based on different 
    /// perspectives
    /// </summary>
    public class ExecutionContextFactory : IExecutionContextFactory
    {
        private readonly ITenantContextFactory _tenantContextFactory;

        public ExecutionContextFactory(ITenantContextFactory tenantContextFactory)
        {
            _tenantContextFactory = tenantContextFactory;
        }

        /// <summary>
        /// Creates an executioncontext for the specified tenant and user
        /// </summary>
        /// <param name="tenant"></param>
        /// <param name="user"></param>
        /// <returns></returns>
        public IExecutionContext FromUser(string tenant, string user)
        {
            return new ExecutionContext(tenant, user, _tenantContextFactory);
        }

        /// <summary>
        /// creates an executioncontext for the specified tenant using the current thread principle for the user
        /// </summary>
        /// <param name="tenant"></param>
        /// <returns></returns>
        public IExecutionContext FromTenant(string tenant)
        {
            return new ExecutionContext(tenant, _tenantContextFactory);
        }

        /// <summary>
        /// Creates an executioncontext for a tenant domain command, query or other domain-orientated object
        /// using the Thredas IPrinicple for the user context 
        /// </summary>
        /// <param name="event"></param>
        /// <returns></returns>
        public IExecutionContext FromDomain(IRequireTenantContext @event)
        {
            return new ExecutionContext(@event.Tenant, _tenantContextFactory);
        }

        /// <summary>
        /// Creates an executioncontext for a domain command, query or other domain-orientated object
        /// </summary>
        public IExecutionContext FromUserDomain(IRequireUserContext @event)
        {
            return new ExecutionContext(@event.Tenant, @event.UserName, _tenantContextFactory);
        }
    }
}
