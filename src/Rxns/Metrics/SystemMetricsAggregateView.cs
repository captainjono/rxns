using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using Rxns.DDD.Commanding;
using Rxns.DDD.CQRS;
using Rxns.Health;
using Rxns.Interfaces;
using Rxns.Playback;

namespace Rxns.Metrics
{
    /// <summary>
    /// This class is responsible for defining the view which feeds to the metrics page
    /// It is simple a collection of event aggregators of different types.
    /// </summary>
    public class SystemMetricsAggregatedView : TimeSeriesAggregatedView
    {
        private readonly ICreateReactors _reactors;

        public override string ReportName
        {
            get { return "SystemMetrics"; }
        }

        public SystemMetricsAggregatedView(ITapeRepository reportRepo, AggViewCfg cfg, ICreateReactors reactors) : base(reportRepo, cfg)
        {
            _reactors = reactors;
        }



        public override IObservable<TimeSeriesData> GetOrCreateStream()
        {
            var metrics = _reactors.GetOrCreate(ReactorManager.DefaultReactorName).Reactor.Pulse.Subscribe(Input);


            var cpu = this.OnReactionTo<CpuSnapshotEvent>().Select(q => new TimeSeriesData() { Name = q.ReporterName, TimeStamp = q.TimeCaptured, Value = q.Average });
            var mem = this.OnReactionTo<MemorySnapshotEvent>().Select(q => new TimeSeriesData() { Name = q.ReporterName, TimeStamp = q.TimeCaptured, Value = q.Average });
            var qrys = this.OnReactionTo<DomainQueryRunning>().Select(q => new TimeSeriesData() { Name = q.ReporterName, TimeStamp = q.TimeCaptured, Value = q.Count });
            var cmds = this.OnReactionTo<DomainCommandRunning>().Select(q => new TimeSeriesData() { Name = q.ReporterName, TimeStamp = q.TimeCaptured, Value = q.Count });
            var overflow = this.OnReactionTo<QueueOverflowEvent>().Select(q => new TimeSeriesData() { Name = q.ReporterName, TimeStamp = q.TimeCaptured, Value = q.TotalItems });
            var snapshot = this.OnReactionTo<QueueSnapshotEvent>().Select(q => new TimeSeriesData() { Name = q.ReporterName, TimeStamp = q.TimeCaptured, Value = q.TotalItems });
            var speed = this.OnReactionTo<QueueSpeedEvent>().Select(q => new TimeSeriesData() { Name = q.ReporterName + "Spd", TimeStamp = q.TimeCaptured, Value = q.TotalItems });
            var timerAverages = this.OnReactionTo<AppOpTimer>()
                .Where(t => t.Max.HasValue)
                .SumBuffered(q => q.Speed.TotalMilliseconds, TimeSpan.FromSeconds(20))
                .Select(q => new TimeSeriesData()
                {
                    //todo: needs to support multple different timeing actions at once
                    Name = q.Item1.Name.Associate(q.Item1.ReporterName) + "Spd",
                    TimeStamp = q.Item1.TimeCaptured,
                    Value = q.Item2 / q.Item3
                });
            var app = this.OnReactionTo<AppResourceInfo>().Buffer(TimeSpan.FromSeconds(20)).Where(p => p.AnyItems()).SelectMany(qq =>
           {
               var q = qq.Last(); //todo: take average
               return new[]
              {
                    new TimeSeriesData() { Name = q.ReporterName.Associate("AppMem"), TimeStamp = q.TimeCaptured, Value = q.MemUsage },
                    new TimeSeriesData() { Name = q.ReporterName.Associate("AppHndls"), TimeStamp = q.TimeCaptured, Value = q.Handles },
                    new TimeSeriesData() { Name = q.ReporterName.Associate("AppThrds"), TimeStamp = q.TimeCaptured, Value = q.Threads },
                    new TimeSeriesData() { Name = q.ReporterName.Associate("AppMem"), TimeStamp = q.TimeCaptured, Value = q.MemUsage },
               };

           });

            //need to implement eq propertly for timeseriesdata.
            return overflow.Merge(snapshot).Merge(cpu).Merge(mem).Merge(speed).Merge(cmds).Merge(app).Merge(qrys).Merge(timerAverages)
                           .Buffer(TimeSpan.FromSeconds(10))
                           .Select(b => b.Distinct(EqualityComparer<TimeSeriesData>.Default))
                           .SelectMany(m => m)
                           .Finally(() => metrics.Dispose())
                           .Select(_ =>
                           {
                               _.Name = _.Name.Replace("Rctr<", "<").Replace(">.L", "].L").Replace(">.s", "].s").Replace("LazyCache<", "[").Replace("default", "dft");
                               return _;
                           });
        }
    }

    public static class ext
    {
        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="items"></param>
        /// <param name="toSum"></param>
        /// <param name="nextSample"></param>
        /// <param name="sampler"></param>
        /// <returns></returns>
        public static IObservable<Tuple<T, double, long>> SumBuffered<T>(this IObservable<T> items, Func<T, double> toSum, TimeSpan nextSample, IScheduler sampler = null)
        {
            double sum = 0;
            long count = 0;

            return items
                .Do(_ =>
                {
                    count++;
                    sum += toSum(_);
                })
                .Sample(nextSample, sampler ?? Scheduler.Default)
                .Select(_ =>
                {
                    var re = new Tuple<T, double, long>(_, sum, count);
                    sum = 0;
                    return re;
                });

        }
        public static TimeSpan Average<T>(this IEnumerable<T> items, Func<T, TimeSpan> selector)
        {
            return TimeSpan.FromMilliseconds(items.Sum(e => selector(e).TotalMilliseconds) / items.Count());
        }
        public static string Associate(this string context, string type)
        {
            return $"{type}.{context}";
        }
    }
}
