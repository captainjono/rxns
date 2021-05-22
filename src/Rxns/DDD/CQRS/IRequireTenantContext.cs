
namespace Rxns.DDD.CQRS
{
    public interface IRequireTenantContext
    {
        string Tenant { get; }
        bool HasTenantSpecified();
        void AssignTenant(string tenant);
        //void ForTenant(string tenant);
    }
}
