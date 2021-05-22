namespace Rxns.DDD.BoundedContext
{
    public interface ITenantInstaller
    {
        void Run(string tenant);
    }
}
