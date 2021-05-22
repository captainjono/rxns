using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Runtime.Serialization;
using Rxns.Collections;
using Rxns.DDD.Tenant;
using Rxns.Interfaces;
using Rxns.Scheduling;

namespace Rxns.DDD.Sql
{
    [DataContract]
    public partial class TenantSqlTask
    {
        /// <summary>
        /// A list of tenant names, or tenant syntax that is shorthand for 
        /// the databases to run the sql against
        /// </summary>
        [DataMember]
        [DefaultValue(default(string[]))]
        public string[] Tenants { get; set; }
    }

    public partial class TenantSqlTask : SqlTask
    {
        public IScheduler DefaultScheduler { get; set; }

        private readonly ITenantDatabaseFactory _dbFactory;

        public TenantSqlTask(IDatabaseConnection database, IFileSystemService fileSystem, IZipService zipService, ITenantDatabaseFactory dbFactory, IDefaultDbCfg tenantConfiguration, IScheduler scheduler = null)
            : base(database, tenantConfiguration, fileSystem, zipService)
        {
            DefaultScheduler = scheduler ?? TaskPoolSchedulerWithLimiter.ToScheduler(Environment.ProcessorCount);
            _dbFactory = dbFactory;
        }

        public IObservable<Unit> Execute(IEnumerable<string> tenants, IEnumerable<string> scripts)
        {
            tenants = tenants ?? Tenants;

            if (tenants == null)
                return Observable.Throw<Unit>(new ArgumentException("tenants"));

            return Rxn.Create(() =>
            {
                tenants
                .ToObservable(DefaultScheduler)
                .SelectMany(tenant =>
                    Observable.Start(() =>
                    {
                        OnInformation("Running script against: {0}", tenant);
                        this.ReportExceptions(() => Execute(scripts, ConnectionString, _dbFactory.GetDatabaseName(tenant)).Wait(), error => OnError(error, "[{0}]: {1}", tenant, error));
                        OnInformation("Script finished against: {0}", tenant);
                    }))
                .ToArray()
                .Wait();
            });
        }

        public override IObservable<Unit> Execute()
        {
            var scripts = GetScriptsAndPrepare(Path).ToArray();
            return Execute(Tenants, scripts);
        }
    }
}
