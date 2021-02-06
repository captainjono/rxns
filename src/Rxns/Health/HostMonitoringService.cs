using System;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Rxns.Cloud;
using Rxns.DDD.Commanding;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns.Health
{
    public class MemorySnapshotEvent : HealthEvent
    {
        public float Average { get; private set; }

        public MemorySnapshotEvent() : base("MEM") { }

        public MemorySnapshotEvent(float average)
            : this()
        {
            Average = average;
        }
    }

    public class ThreadsSnapshotEvent : HealthEvent
    {
        public float Count { get; private set; }

        public ThreadsSnapshotEvent() : base("THREADS") { }

        public ThreadsSnapshotEvent(float average)
            : this()
        {
            Count = average;
        }
    }

    public class CpuSnapshotEvent : HealthEvent
    {
        public float Average { get; private set; }

        public CpuSnapshotEvent() : base("CPU") { }

        public CpuSnapshotEvent(float average)
            : this()
        {
            Average = average;
        }
    }

    public class HostMonitoringService : ReportStatusService, IRxnPublisher<IRxn>, IReportHealth, IRxnCfg
    {
        public ISubject<IHealthEvent> Pulse { get; private set; }

        private readonly ISystemResourceService _systemResources;
        private readonly IScheduler _resourceSampler;
        private Action<IRxn> _publishEvent;
        private float _cpuAverage;
        private float _memAverage;
        private float _ourMemAverage;
        private int _thrdAverage;

        public string Reactor { get; private set; }
        public IObservable<IRxn> ConfigureInput(IObservable<IRxn> pipeline)
        {
            return pipeline;
        }

        public IDeliveryScheme<IRxn> InputDeliveryScheme { get; private set; }
        public bool MonitorHealth { get { return true; } }
        public RxnMode Mode { get; }

        public HostMonitoringService(ISystemResourceService systemResources, IScheduler resourceSampler = null)
        {
            _systemResources = systemResources;
            _resourceSampler = resourceSampler ?? RxnSchedulers.Default;

            Pulse = new Subject<IHealthEvent>();
        }

        public void ConfigiurePublishFunc(Action<IRxn> publish)
        {
            _publishEvent = publish;
        }

        public override IObservable<CommandResult> Stop(string @from = null)
        {
            return Rxn.Create(() => StopMonitoring()).Select(_ => CommandResult.Success());
        }

        public override IObservable<CommandResult> Start(string @from = null, string options = null)
        {
            return Rxn.Create(() => StartMonitoring()).Select(_ => CommandResult.Success());
        }

        private void StartMonitoring()
        {
            //need to start work on the UI stuff now --> i want to create a small report? or just display numbers?
            _systemResources.CpuUsage.Buffer(TimeSpan.FromSeconds(20), _resourceSampler)
                                    .Select(cpu => _cpuAverage = cpu.Sum() / cpu.Count)
                                    .Do(cpu => Pulse.OnNext(new CpuSnapshotEvent(cpu)))
                                    .Subscribe()
                                    .DisposedBy(this);

            _systemResources.MemoryUsage.Buffer(TimeSpan.FromSeconds(20), _resourceSampler)
                                        .Select(mem => _memAverage = mem.Sum() / mem.Count)
                                        .Do(mem => Pulse.OnNext(new MemorySnapshotEvent(mem)))
                                        .Where(mem => mem >= 0.95)
                                        .DoBuffered(mem => OnError("Memory alert @ {0}%", mem * 100), TimeSpan.FromMinutes(10))
                                        .Subscribe()
                                        .DisposedBy(this);

            //_systemResources.AppUsage.Buffer(TimeSpan.FromSeconds(20), _resourceSampler)
            //                          .Select(mem => _memAverage = mem..Sum() / mem.Count)
            //                          .Do(mem => Pulse.OnNext(new AppInfoEvent(mem)))
            //                          .Where(mem => mem >= 0.95)
            //                          .DoBuffered(mem => OnError("Memory alert @ {0}%", mem * 100), TimeSpan.FromMinutes(10))
            //                          .Subscribe()
            //                          .DisposedBy(this);

            _publishEvent(new AppStatusInfoProviderEvent()
            {
                Info = () =>
                        new
                        {
                            CpuAverage = _cpuAverage,
                            MemAverage = _memAverage,
                         //   Threads = _threadAv,
                        //    Handles = _threadAv,
                         //   AppMem = _threadAv,
                            AppThreadsMax = RxnSchedulers.ThreadPoolSize,
                            AppThreadsSize = RxnSchedulers.TaskSchedulerMeta.PoolCurrent
                        }
            });
        }

        private void StopMonitoring()
        {
            ManagedResources.DisposeAll();
            ManagedResources.Clear();
        }

        public void Shock()
        {

        }

    }
}
