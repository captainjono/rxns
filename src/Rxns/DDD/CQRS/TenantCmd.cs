using System;

namespace Rxns.DDD.CQRS
{
    public class TenantCmd<T> : IDomainCommand<T>, IRequireTenantContext
    {
        public string Id { get; set; }
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
