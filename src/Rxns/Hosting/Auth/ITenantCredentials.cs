namespace Rxns.Hosting
{
    public interface ITenantCredentials
    {
        string Tenant { get; set; }
        string Key { get; set; }
    }
}
