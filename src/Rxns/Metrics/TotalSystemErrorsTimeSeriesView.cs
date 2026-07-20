using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using Rxns.Microservices;
using Rxns.Playback;

namespace Rxns.Metrics
{
    public class ErrorsTimesSeriesView : TimeSeriesAggregatedView
    {
        private readonly IAppContainer _container;

        public ErrorsTimesSeriesView(ITapeRepository reportRepo, AggViewCfg reportDirectory, IAppContainer container) : base(reportRepo, reportDirectory)
        {
            _container = container;
        }

        public override string ReportName
        {
            get { return "SystemErrors"; }
        }

        public override IObservable<TimeSeriesData> GetOrCreateStream()
        {
            return this.OnReactionTo<TenantError>().Select(_ => _.Error)
                                                   .Merge(_container.Errors.Select(e =>
                                                    {
                                                        var stackTrace = e.Message.StackTrace != null ? e.Message.StackTrace.Split(new[] { "at" }, StringSplitOptions.RemoveEmptyEntries).Take(2).ToStringEach(" ") : "no stack trace";
                                                        return "{0}:{1}".FormatWith(e.Message.Message, stackTrace.Serialise()); //was 
                                                    }))
                                                    .Buffer(TimeSpan.FromMinutes(1))
                                                    .Where(e => e.Count > 0)
                                                    .SelectMany(total => new[] {
                                                        new TimeSeriesData
                                                        {
                                                            Name = "Errors",
                                                            TimeStamp = DateTime.Now,
                                                            Value = new
                                                            {
                                                                Total = total.Count,
                                                                Events = total.Count,
                                                                Errors = "All errors"
                                                            }
                                                        }
                                                    }.Concat(total.Aggregate(new Dictionary<string, int>(), (dict, error) =>
                                                    {
                                                        if (dict.ContainsKey(error))
                                                        {
                                                            dict[error] = dict[error]++;
                                                        }
                                                        else
                                                        {
                                                            dict.Add(error, 1);
                                                        }
                                                        return dict;
                                                    }).Select(error => new TimeSeriesData
                                                    {
                                                        Name = error.Key.Split('\r')[0].Trim(),
                                                        TimeStamp = DateTime.Now,
                                                        Value = new
                                                        {
                                                            Total = error.Value,
                                                            Events = 0,
                                                            Message = error.Key
                                                        }
                                                    })));
        }
    }
}
