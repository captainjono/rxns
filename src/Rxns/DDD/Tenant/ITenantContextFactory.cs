namespace Rxns.DDD.Tenant
{
    public interface ITenantContextFactory
    {
        IUserContext GetUserContext(string tenant, string userName = null);
        ITenantContext GetContext(string tenant);
    }
}
