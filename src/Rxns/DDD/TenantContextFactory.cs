using System;
using Rxns.DDD.Tenant;

namespace Rxns.DDD
{
    /// <summary>
    /// todo: add caching to getters ?
    /// </summary>
    public class TenantContextFactory : ITenantContextFactory
    {

        public Func<string, ITenantContext> _tenantFactory { get; set; }
        private readonly IUserContextFactory _userContextFactory;

        public TenantContextFactory(IUserContextFactory userContextFactory, Func<string, ITenantContext> tenantFactory)
        {

            _tenantFactory = tenantFactory;
            _userContextFactory = userContextFactory;
        }


        public IUserContext GetUserContext(string tenant, string userName = null)
        {
            return _userContextFactory.GetUserContext(tenant, userName);
        }

        public ITenantContext GetContext(string tenant)
        {
            return _tenantFactory(tenant);
        }
    }


}  