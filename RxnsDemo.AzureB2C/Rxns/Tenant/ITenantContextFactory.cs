namespace RxnsDemo.AzureB2C.Rxns
{
    public interface ITenantContextFactory
    {
        IUserContext GetUserContext(string tenant, string userName = null);
        ITenantContext GetContext(string tenant);
    }
}
