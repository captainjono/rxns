using System;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using Rxns;
using Rxns.Cloud;
using Rxns.DDD.BoundedContext;
using Rxns.DDD.Commanding;
using Rxns.Interfaces;
using RxnsDemo.AzureB2C.RxnApps.Events;

namespace RxnsDemo.AzureB2C.RxnApps
{
    public class InProcessImportOrchestrator : ShardingQueueProcessingService<StartImportOfUsersIntoTenantCmd>
    {
        private readonly ICurrentTenantsService _tenants;

        public InProcessImportOrchestrator(ICurrentTenantsService tenants, IScheduler queueWorkerScheduler = null)
            : base("ImportProgress", true, queueWorkerScheduler)
        {
            _tenants = tenants;
        }

        public override IObservable<CommandResult> Start(string @from = null, string options = null)
        {
            return Run(() =>
            {
                var allTenants = _tenants.ActiveTenants.WaitR()
                    .Select(tenant =>
                    {
                        var t = tenant;
                        return new Func<StartImportOfUsersIntoTenantCmd, bool>(i =>
                        {
                            var tt = t;
                            return i.Tenant == tt;
                        });
                    })
                    .ToArray();

                _queueWorkerScheduler = TaskPoolSchedulerWithLimiter.ToScheduler(allTenants.Length > 0 ? allTenants.Length : 8);
                StartQueue(allTenants);
            });
        }

        public override void OnQueued(StartImportOfUsersIntoTenantCmd cmd)
        {
            OnInformation($"<{cmd.Id}> Queued import of Users into '{cmd.Tenant}''", cmd.Id, cmd.Tenant);

            _publish(new ImportOfUsersIntoTenantQueuedEvent(cmd.Tenant, cmd.Id));
        }

        protected override IObservable<IRxn> ProcessQueueItem(StartImportOfUsersIntoTenantCmd cmd)
        {
            return Rxn.Create<IRxn>(() =>
            {
                OnInformation("[{0}] Processing import of {1} users into '{2}''", cmd.Id, cmd.Users.Length, cmd.Tenant);
                _publish(new ImportOfUsersIntoTenantStartedEvent(cmd.Tenant, cmd.Id));
            })
            .SelectMany(Rxn.Create(() =>
            {
                return true;
            }))
            .Select(_ => new ImportOfUsersIntoTenantStagedEvent(cmd.Tenant, cmd.Id))
            .Finally(() =>
            {
                OnVerbose("[{0}] Finished processing import of users into '{2}''", cmd.Id, cmd.Tenant);
            });
        }
    }
}
