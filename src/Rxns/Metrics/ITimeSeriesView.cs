using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reactive;
using System.Reactive.Linq;
using Rxns.Cloud;
using Rxns.Collections;
using Rxns.DDD.Commanding;
using Rxns.Interfaces;
using Rxns.Logging;


namespace Rxns.Metrics
{
    public interface ITimeSeriesView : IReactTo<IRxn>
    {
        string ReportName { get; }
        IObservable<TimeSeriesData> GetHistory();
        IObservable<TimeSeriesData> GetUpdates();
    }

   



    public class LogAction : ITimeSeriesWatchAction
    {
        public IObservable<Unit> Peform(TimeSeriesData data)
        {
            return Rxn.Create(() => Debug.WriteLine("<<<LOG ACTION>>>>"));
        }
    }


    public class WhenGreaterThen : ITimeSeriesWatchCondition
    {
        private readonly string _property;
        private readonly long _number;

        public WhenGreaterThen(string property, string number)
        {
            _property = property;
            _number = number.AsLong();
        }

        public WhenGreaterThen(string number)
        {
            _number = number.AsLong();
        }

        public bool When(TimeSeriesData data)
        {
            return (long) (_property == null ? data.Value : data.Value.GetProperty(_property)) > _number;
        }
    }

    public class WhenReportIs : ServiceCommand
    {
        public string Name { get; private set; }
        public string TimeSeriesType { get; private set; }
        public string WhenConditionType { get; private set; }
        public string PerformActionType { get; private set; }

        public WhenReportIs(string name, string timeSeriesType, string when, string perform)
        {
            Name = name;
            TimeSeriesType = timeSeriesType;
            WhenConditionType = when;
            PerformActionType = perform;
        }
    }

    public class StopWhenReportIs : ServiceCommand
    {
        public string Name { get; set; }

        public StopWhenReportIs(string name)
        {
            Name = name;
        }
    }


    public interface ITimeSeriesWatcherCfg
    {
        string Name { get; }
        IObservable<TimeSeriesData> Stream { get; }
        Func<TimeSeriesData, bool> When { get; }
        Func<TimeSeriesData, IObservable<Unit>> Perform { get; }
    }

    public class StaticWatcherCfg : ITimeSeriesWatcherCfg
    {
        public string Name { get; set; }
        public IObservable<TimeSeriesData> Stream { get; set; }

        public Func<TimeSeriesData, bool> When { get; set; }

        public Func<TimeSeriesData, IObservable<Unit>> Perform { get; set; }
    }

    
}
