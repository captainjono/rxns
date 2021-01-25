namespace Rxns.Hosting
{
    public interface IAccessTokenFactory
    {
        T FromJson<T>(string json) where T : AccessToken;
    }
}
