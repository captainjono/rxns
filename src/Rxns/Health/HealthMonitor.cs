using System;

namespace Rxns.Health
{
    public static class HealthMonitor
    {
        public static IMonitorActionFactory<T>[] ForQueue<T>(IReportHealth reporter, string name = null, long snapshotWhen = 0, long overflowWhen = 500, TimeSpan? sampleTime = null)
        {
            name = name == null ? reporter.ReporterName : "{0}.{1}".FormatWith(reporter.ReporterName, name);

            var overflow = new BackpressureAction<T>(c => c > overflowWhen, (t, current) => reporter.Pulse.OnNext(new QueueOverflowEvent(name, current)), TimeSpan.FromMinutes(5));
            var snapshot = new BackpressureAction<T>(c => c > snapshotWhen, (t, current) => reporter.Pulse.OnNext(new QueueSnapshotEvent(name, TimeSpan.FromSeconds(1), current)), sampleTime ?? TimeSpan.FromSeconds(15));
            var countPerSec = new StreamTimerAction<T>((count) => reporter.Pulse.OnNext(new QueueSpeedEvent(name, count)), sampleTime ?? TimeSpan.FromSeconds(15));


            return new IMonitorActionFactory<T>[]
            {
                snapshot,
                overflow,
                countPerSec
            };
        }

        public static IDisposable ReportsWith(this IReportHealth reporter, IReportHealth target)
        {
            return reporter.Pulse.Subscribe(p => target.Pulse.OnNext(p.Tunnel(target)));
        }
    }
}
