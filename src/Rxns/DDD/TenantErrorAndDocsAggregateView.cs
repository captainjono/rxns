using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using Rxns.Metrics;
using Rxns.Playback;

namespace Rxns.DDD
{
    /// <summary>
    /// This class is still a work in progress. its going to count tenant errors and other things
    /// </summary>
    public class TenanErrorsAggregateView : TimeSeriesAggregatedView
    {
        public override string ReportName
        {
            get { return "TenantAndMore"; }
        }

        public override IObservable<TimeSeriesData> GetOrCreateStream()
        {
            //was looking for way to stop boradcasting when a value has not changed as its overloading the servers memory.
            //need to implement eq propertly for timeseriesdata.
            var random = new Random();
            Func<TimeSpan> bufferTime = () => TimeSpan.FromSeconds(random.Next(10, 30));
            var error = this.OnReactionTo<TenantError>().Select(q => new TimeSeriesData() { Name = q.Message, TimeStamp = q.Timestamp, Value = 1 });
            //var speed = this.OnReactionTo<DocumentUploaded>().Select(q => new TimeSeriesData() { Name = q.Tenant, TimeStamp = q.Timestamp, Value = 1 });

            return error
                .Buffer(TimeSpan.FromSeconds(10))
                .Select(b => b.Distinct(EqualityComparer<TimeSeriesData>.Default))
                .SelectMany(m => m);
        }

        public TenanErrorsAggregateView(ITapeRepository reportRepo, AggViewCfg cfg) : base(reportRepo, cfg)
        {
        }
    }
}
