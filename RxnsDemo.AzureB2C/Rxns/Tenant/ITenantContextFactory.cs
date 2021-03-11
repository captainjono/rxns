namespace RxnsDemo.AzureB2C.Rxns.Tenant
{
    public interface ITenantContextFactory
    {
        IUserContext GetUserContext(string tenant, string userName = null);
        ITenantContext GetContext(string tenant);
    }
}
