namespace Rxns.WebApiNET5.NET5WebApiAdapters
{
    public interface IAggSyncServerConfig : IAggSyncConfig
    {
        string SupportServicesBaseUrl { get; }
        string SupportUserName { get; }
        string SupportPassword { get; }
        string LocalWebServicesUrl { get; }
        string SourceStorageConnectionString { get; }
        //if null or empty, all active tenants will be sync'd
        string[] TenantsToSync { get; }
    }
}
