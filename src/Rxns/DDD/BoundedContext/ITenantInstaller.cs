namespace Rxns.DDD.BoundedContext.Tenant
{
    public interface ITenantInstaller
    {
        void Run(string tenant);
    }
}
