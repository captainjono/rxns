using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using Rxns.Cloud;
using Rxns.Health;
using Rxns.Health.AppStatus;
using Rxns.Hosting.Updates;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns.Hosting
{
    /// <summary>
    /// IAppStatusServiceClient impl for the self-hosted central / arena scenario:
    /// "I am the central — there is no upstream to forward to".
    ///
    /// <para>
    /// Why this exists: the default <see cref="HttpAppStatusServiceClient"/> POSTs every
    /// event to /events/publish. On a self-hosted arena that POST lands on the arena's
    /// OWN events controller, which republishes onto the local bus, the router fans it
    /// back to <see cref="AppStatusBackingChannel"/>, which buffers + POSTs again —
    /// infinite feedback loop with a 2s cadence (visible as e.g. RemoteShellResult
    /// re-emitted every 2s on the Remote Shell view).
    /// </para>
    ///
    /// <para>
    /// <see cref="Publish"/>/<see cref="PublishError"/>/<see cref="PublishLog"/>/<see cref="DeleteError"/>
    /// are no-ops: events originating on the arena are already on the local bus, no
    /// forwarding needed.
    /// </para>
    ///
    /// <para>
    /// <see cref="PublishSystemStatus"/> writes directly to <see cref="ISystemStatusStore"/>
    /// in-process. Without this, <see cref="Health.AppStatus.SystemStatusPublisher"/>'s
    /// per-tick call to it would no-op, the arena would never appear in its own
    /// SystemStatusStore, and #/appStatusV2 would show only workers (regression caught
    /// by AppStatusV2Behaviour.arena_and_worker_heartbeats_reach_system_status_store).
    /// We take ISystemStatusStore directly rather than IAppStatusManager because the
    /// latter pulls in IAppUpdateManager → LocalAppUpdateServer → IRxnHostableApp,
    /// which isn't registered on the arena.
    /// </para>
    /// </summary>
    public class NoOpAppStatusServiceClient : ReportsStatus, IAppStatusServiceClient
    {
        private readonly ISystemStatusStore _store;

        public NoOpAppStatusServiceClient(ISystemStatusStore store)
        {
            _store = store;
        }

        public IObservable<Unit> Publish(IEnumerable<IRxn> events) =>
            Observable.Return(Unit.Default);

        public IObservable<Unit> PublishError(BasicErrorReport report) =>
            Observable.Return(Unit.Default);

        public IObservable<Unit> DeleteError(long id) =>
            Observable.Return(Unit.Default);

        public IObservable<string> PublishLog(Stream zippedLog) =>
            Observable.Return(string.Empty);

        public IObservable<IRxnQuestion[]> PublishSystemStatus(SystemStatusEvent status, AppStatusInfo[] meta)
        {
            // Mirror LocalAppStatusManager.AddOrUpdateStatus's log line so existing
            // tooling (TestArenaMonitor.ps1 -ArenaStatus, AppStatusV2Behaviour) still
            // sees "Received status from '<tenant>\<system>'" for the arena's own
            // heartbeat — the canonical signal that the store now holds an entry for
            // this system.
            OnInformation("Received status from '{0}\\{1}'", status.Tenant, status.SystemName);
            var metaAsDynamic = (meta ?? Array.Empty<AppStatusInfo>()).Cast<dynamic>().ToArray();
            return _store.AddOrUpdate(status, metaAsDynamic)
                .Select(_ => Array.Empty<IRxnQuestion>());
        }
    }
}
