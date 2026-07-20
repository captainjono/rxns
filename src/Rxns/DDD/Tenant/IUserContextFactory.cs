namespace Rxns.DDD.Tenant
{
    public interface IUserContextFactory
    {
        IUserContext GetUserContext(string tenant, string userName = null);
    }
}
