using Rxns.DDD.BoundedContext;

namespace Rxns.DDD.Tenant
{
    public interface ITenantContext
    {
        //Lazy<TenantSettings> Cfg { get; }
        string Tenant { get; }
        
        ITenantDatabaseFactory DatabaseContext { get; }
        ITenantDiscardRepository DiscardContext { get; }
        
        //IDictionary<string, Document> Documents { get; }
        //Users { get; }
    }
}
