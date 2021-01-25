namespace Rxns.Hosting
{
    public interface IUserCredentials : ITenantCredentials
    {
        string Username { get; set; }
        string Password { get; set; }
        string RefreshToken { get; set; }
    }
}
