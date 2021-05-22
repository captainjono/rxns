using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using Rxns.DDD.BoundedContext;
using Rxns.DDD.Sql;
using Rxns.DDD.Tenant;
using Rxns.Hosting.Updates;
using Rxns.Scheduling;

namespace Rxns.DDD
{
    public class TenantInstaller : DefaultSystemInstaller, ITenantInstaller
    {
        protected string _target;
        private static readonly object _locker = new object();
        protected int _sourceSystem;

        public TenantInstaller(IInstallerConfiguration configuartion, ITaskProvider setupTasks, ITaskScheduler taskRunner, IAppUpdateManager manager, IScheduler defaultScheduler = null)
            : base(configuartion, setupTasks, taskRunner, manager, defaultScheduler)
        {
            ShouldDisposeOfInstallTasks = false;
        }

        protected override void Rollback()
        {
            //stop log from being uploaded
        }

        protected override IObservable<Unit> RunInstallTasks(ITaskScheduler scheduler, ISchedulableTaskGroup tasks)
        {
            var tenantTasks = tasks.Tasks.OfType<TenantSqlTask>().ToArray();

            //add or update the source system
            var sourceSystemParam = tasks.Parameters.FirstOrDefault(w => w.Name.Equals("SourceSystem", StringComparison.InvariantCultureIgnoreCase));

            if (sourceSystemParam != null)
                sourceSystemParam.Value = _sourceSystem;
            else
                tasks.Parameters.AddOrReplace(new OutputParameter() { Name = "SourceSystem", Value = _sourceSystem });

            foreach (var task in tenantTasks)
            {
                task.Tenants = new[] { _target };
            }

            return tenantTasks.AnyItems() ? scheduler.Run(tasks) : new Unit().ToObservable();
        }

        public void Run(string tenant)
        {
            lock (_locker)
            {
                _target = tenant;
                Install();
            }
        }
    }
}
