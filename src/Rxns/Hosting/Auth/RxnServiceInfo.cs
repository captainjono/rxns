namespace Rxns.Hosting.Auth
{
    public class RxnServiceInfo : IUserCredentials
    {
        public string Tenant { get; set; }
        public string Key { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string RefreshToken { get; set; }
    }
}
