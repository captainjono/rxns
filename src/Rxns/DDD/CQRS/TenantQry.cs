using System;
using Rxns.CQRS;

namespace Rxns.DDD.CQRS
{
    public class TenantQry<TResult> : IDomainQuery<TResult>, IRequireTenantContext
    {
        public string Id { get; set; }
        public string Tenant { get; set; }


        public TenantQry()
        {
            Id = Guid.NewGuid().ToString();
        }
        public TenantQry(string tenant) : base()
        {
            
            Tenant = tenant;
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
