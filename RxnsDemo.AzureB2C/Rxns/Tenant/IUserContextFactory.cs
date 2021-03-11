namespace RxnsDemo.AzureB2C.Rxns.Tenant
{
    public interface IUserContextFactory
    {
        IUserContext GetUserContext(string tenant, string userName = null);
    }
}
