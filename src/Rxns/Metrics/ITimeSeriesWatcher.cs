using System;
using System.Reactive;

namespace Rxns.Metrics
{
    public interface ITimeSeriesWatchAction
    {
        IObservable<Unit> Peform(TimeSeriesData data);
    }
    public interface ITimeSeriesWatchCondition
    {
        bool When(TimeSeriesData data);
    }
    public interface ITimeSeriesWatcher
    {
        IDisposable Watch(IObservable<TimeSeriesData> stream, Func<TimeSeriesData, Boolean> when, Func<TimeSeriesData, IObservable<Unit>> perform);
    }
}
