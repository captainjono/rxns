using Rxns.AppStatus.Host.Monitor.Sources;
using Rxns.DDD;
using Rxns.DDD.BoundedContext;
using Rxns.DDD.Tenant;
using Rxns.Hosting;
using Rxns.Microservices;

namespace Rxns.AppStatus.Host.Monitor
{
    /// <summary>
    /// Wires the monitor backend: <see cref="DDDServerModule"/> (for the
    /// aggregate persistence pipeline), the <see cref="MonitorRoot"/>
    /// repository, the built-in <see cref="BusLogSource"/>, and the
    /// <see cref="MonitorService"/> singleton.
    ///
    /// <para>Persistence backend: V1 uses the stock in-memory tape repo from
    /// <see cref="DDDServerModule"/> (suggestions/trust list survive within
    /// a host process but reset on restart). To persist across restarts,
    /// override <c>Func&lt;string, ITapeSource&gt;</c> with a disk-backed
    /// factory that writes under <c>.rxns/MonitorRoot/</c> — that single
    /// registration upgrades persistence without touching any other code
    /// in this module.</para>
    ///
    /// <para>Augmentation modules (a consumer app's support portal)
    /// register their own <see cref="IMonitorSource"/> impls
    /// — they show up automatically as additional source checkboxes in the
    /// UI, no framework change required.</para>
    /// </summary>
    public class MonitorModule : IAppModule
    {
        public IRxnLifecycle Load(IRxnLifecycle lifecycle)
        {
            return lifecycle
                .Includes<DDDServerModule>()

                // MonitorRoot repository — TapeArrayTenantModelRepository wants
                // (ITapeArrayFactory, ITenantModelFactory<MonitorRoot>, Func<TR,string>).
                // The first two come from DDDServerModule; the selector returns
                // a constant since we only keep one stream per tenant.
                .CreatesOncePerApp<ITenantModelRepository<MonitorRoot>>(c =>
                    new TapeArrayTenantModelRepository<MonitorRoot, Rxns.DDD.BoundedContext.IDomainEvent>(
                        c.Resolve<Rxns.Playback.ITapeArrayFactory>(),
                        c.Resolve<ITenantModelFactory<MonitorRoot>>(),
                        _ => "events"))

                // Built-in source — polls the rxns log bus for errors.
                .CreatesOncePerAppAs<BusLogSource, IMonitorSource>()

                // Orchestrator. Resolved on the first /api/monitor/* call;
                // ctor wires subscriptions + the flush ticker so once anything
                // resolves it, monitor mode is live.
                .CreatesOncePerApp<MonitorService>();
        }
    }
}
