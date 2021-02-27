namespace RxnsDemo.AzureB2C.Rxns
{
    public interface IUserContextFactory
    {
        IUserContext GetUserContext(string tenant, string userName = null);
    }
}
