using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using Autofac.Features.OwnedInstances;
using Rxns;
using Rxns.Logging;
using Rxns.Scheduling;
using RxnsDemo.AzureB2C.Rxns.Tenant;

namespace RxnsDemo.AzureB2C.Rxns.Sql
{
    public class SqlTenantDatabaseFactory : ITenantDatabaseFactory
    {
        private readonly ITenantDatabaseConfiguration _configuration;
        private readonly Func<Owned<SqlTask>> _executeFactory;
        private readonly Func<Owned<IDatabaseConnection>> _databaseFactory;
        private readonly ISecurityMode _securityMode;
        private readonly Func<Owned<StoredProcTask>> _storedProcFactory;
        private readonly Dictionary<string, IOrmTransactionContext> _connectioCache = new Dictionary<string, IOrmTransactionContext>();

        private readonly object _singleThread = new object();

        public SqlTenantDatabaseFactory(ITenantDatabaseConfiguration configuration, Func<Owned<SqlTask>> executeFactory, Func<Owned<StoredProcTask>> storedProcFactory, Func<Owned<IDatabaseConnection>> databaseFactory, ISecurityMode securityMode)
        {
            _configuration = configuration;
            _executeFactory = executeFactory;
            _databaseFactory = databaseFactory;
            _storedProcFactory = storedProcFactory;
            _securityMode = securityMode;
        }

        public string GetDatabaseConnectionString(string tenant)
        {
            if (string.IsNullOrEmpty(_configuration.SqlUsername))
            {
                throw new Exception($"No sql connection string configured for {tenant}");
            }

            var cs = new SqlConnectionStringBuilder();
            cs.DataSource = _configuration.SqlServer;
            cs.InitialCatalog = GetDatabaseName(tenant);
            cs.UserID = _configuration.SqlUsername;
            cs.Password = _configuration.SqlPassword;
            
            if (_securityMode.IsSecure && _configuration.SqlServer != ".")
            {
                cs.Encrypt = true;
                cs.TrustServerCertificate = false;
            }

            return cs.ToString();
        }

        public string GetDatabaseName(string tenant)
        {
            return _configuration.DbNameFormat.FormatWith(tenant);
        }

        public string GetTenantFromDatabaseName(string database)
        {
            var db = (database ?? "").Split('-'); //hacky but meh
            return db.Length > 0 ? db[1] : database;
        }

        public string GetAdminUserFor(string tenant)
        {
            return "Admin{0}".FormatWith(tenant);
        }

        public Owned<SqlTask> GetExecutionContext(string tenant)
        {
            var task = _executeFactory();
            task.Value.ConnectionString = GetDatabaseConnectionString(tenant);
            return task;
        }

        public Owned<StoredProcTask> GetStoredProcedureContext(string tenant)
        {
            var task = _storedProcFactory();
            task.Value.ConnectionString = GetDatabaseConnectionString(tenant);
            return task;
        }

        public Owned<IDatabaseConnection> GetDatabaseContext(string tenant)
        {
            return _databaseFactory();
        }

        public IOrmTransactionContext GetContext(string tenant)
        {
            lock (_singleThread)
            {
                Ensure.NotNull(tenant, "the tenant has not been specified");

                if (_connectioCache.ContainsKey(tenant))
                    return _connectioCache[tenant];

                var context = new LegacyOrmContext(GetDatabaseConnectionString(tenant));
                _connectioCache.AddOrReplace(tenant, context);
                return context;
            }
        }

        public ITenantUserContext GetUsersContext(IOrmContext tenantDatabase)
        {
            return new SqlMembershipBasedUserContext(tenantDatabase);
        }
    }
}
