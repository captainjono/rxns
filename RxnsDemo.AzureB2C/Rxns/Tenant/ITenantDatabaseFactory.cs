using Autofac.Features.OwnedInstances;
using Rxns.Scheduling;

namespace RxnsDemo.AzureB2C.Rxns.Tenant
{
    public interface ITenantDatabaseFactory
    {
        string GetDatabaseConnectionString(string tenant);
        string GetDatabaseName(string tenant);
        string GetTenantFromDatabaseName(string databaseName);
        string GetAdminUserFor(string tenant);

        Owned<SqlTask> GetExecutionContext(string tenant);
        Owned<StoredProcTask> GetStoredProcedureContext(string tenant);
        Owned<IDatabaseConnection> GetDatabaseContext(string tenant);
        IOrmTransactionContext GetContext(string tenant);
        ITenantUserContext GetUsersContext(IOrmContext tenantDatabase);
    }
}
