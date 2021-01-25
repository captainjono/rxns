using System;
using Rxns.DDD.Commanding;
using Rxns.DDD.CQRS;

namespace Rxns.CQRS
{
    public class TenantCmd<T> : IDomainCommand<T>, IRequireTenantContext
    {
        public string Id { get; private set; }
        public string Tenant { get; set; }

        public TenantCmd()
        {

        }
        public TenantCmd(string tenant)
        {
            Tenant = tenant;
            Id = Guid.NewGuid().ToString();
        }

        public bool HasTenantSpecified()
        {
            return !string.IsNullOrWhiteSpace(Tenant);
        }

        public void AssignTenant(string tenant)
        {
            Tenant = tenant;
        }

        public void ForTenant(string tenant)
        {
            Tenant = tenant;
        }
    }
}
