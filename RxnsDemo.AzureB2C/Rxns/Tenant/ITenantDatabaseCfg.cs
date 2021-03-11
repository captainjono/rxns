namespace RxnsDemo.AzureB2C.Rxns.Tenant
{

    public interface ITenantDatabaseConfiguration
    {
        /// <summary>
        /// The sql seRxner address to connect the repos to
        /// </summary>
        string SqlServer { get; }
        /// <summary>
        /// The username for the connection
        /// </summary>
        string SqlUsername { get; }
        /// <summary>
        /// The password for the connection
        /// </summary>
        string SqlPassword { get; }

        string DbNameFormat {get;}
    }
}
