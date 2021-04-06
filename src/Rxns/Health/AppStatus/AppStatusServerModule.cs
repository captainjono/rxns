using System;
using System.Collections.Generic;
using Rxns.Collections;
using Rxns.DDD;
using Rxns.Hosting;
using Rxns.Hosting.Updates;
using Rxns.Interfaces;
using Rxns.Metrics;
using Rxns.Playback;
using Rxns.WebApi.AppStatus;

namespace Rxns.Health.AppStatus
{
    public interface IReportConnectionManager
    {
        IEnumerable<ReportUser> OnlineUsers { get; }
        IObservable<ReportUser> Connected { get; }
        IObservable<ReportUser> Disconnected { get; }
    }

    public class AppStatusServerModule : IAppModule
    {
        public IRxnLifecycle Load(IRxnLifecycle lifecycle)
        {
            return lifecycle
                .CreatesOncePerApp<AppCommandService>()
                .CreatesOncePerApp(c => new SystemSystemStatusStore(
                    c.ResolveTag<IKeyValueStore<string, SystemSystemStatusStore.StringStore>>("appstatus"),
                    c.ResolveTag<IKeyValueStore<string, SystemSystemStatusStore.StringStore>>("appstatusKeys")
                ))
                .CreatesOncePerAppNamed<DictionaryKeyValueStore<string, SystemSystemStatusStore.StringStore>, IKeyValueStore<string, SystemSystemStatusStore.StringStore>>(
                    () => new DictionaryKeyValueStore<string, SystemSystemStatusStore.StringStore>(), "appstatus")
                .CreatesOncePerAppNamed<DictionaryKeyValueStore<string, SystemSystemStatusStore.StringStore>, IKeyValueStore<string, SystemSystemStatusStore.StringStore>>(
                    () => new DictionaryKeyValueStore<string, SystemSystemStatusStore.StringStore>(), "appstatusKeys")
                .CreatesOncePerApp<Func<string, string, IKeyValueStore<string, long>>>(c =>
                {
                    return (table, part) => new DictionaryKeyValueStore<string, long>();
                })
                .EmitsAnyIn<CpuSnapshotEvent>()
                .CreatesOncePerApp(c => new RealtimeReportStream(c.ResolveTag<ITimeSeriesView>("system"), c.Resolve<IReportConnectionManager>()))
                //for diagnostic portal
                .CreatesOncePerApp<FileSystemTapeRepository>(true)
                .CreatesOncePerApp<InMemoryAppStatusStore>()
                .CreatesOncePerApp<AppStatusCfg>(true)
                .CreatesOncePerApp<ErrorsTapeRepository>()
                //reporting
                .CreatesOncePerApp<TimeSeriesNotificationManager>()
                .CreatesOncePerApp<WhenGreaterThen>()
                .CreatesOncePerApp<LogAction>()
                .RespondsToSvcCmds<StartReactor>()
                .CreatesOncePerApp<ErrorsTimesSeriesView>()
                .CreatesOncePerAppNamed<SystemMetricsAggregatedView, ITimeSeriesView>("system")
                //reliability
                .CreatesOncePerApp<HostMonitoringService>()
                .CreatesOncePerApp<SystemResourceLogger>()
                .CreatesOncePerApp<DotNetThreadPoolThresdholdInitialiser>()
                .CreatesOncePerApp<GetOrCreateAppStatusStoreStartupTask>()
                .CreatesOncePerApp<FileSystemAppUpdateRepo>()
                .CreatesOncePerApp<LocalAppStatusManager>()
                .CreatesOncePerApp<LocalAppErrorManager>()
                .CreatesOncePerApp<CurrentDirectoryAppUpdateStore>()
                ;
        }
    }
}
