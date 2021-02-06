using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reactive;
using System.Reactive.Linq;
using Rxns.Cloud;
using Rxns.Collections;
using Rxns.DDD.Commanding;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns.Metrics
{
    public class TimeSeriesNotificationManager : ReportStatusService, ITimeSeriesWatcher, IServiceCommandHandler<WhenReportIs>, IServiceCommandHandler<StopWhenReportIs>, IRxnPublisher<IRxn>
    {
        private readonly IResolveTypes _typeResolver;
        private readonly ITimeSeriesWatcherCfg[] _cfg;
        private readonly IDictionary<string, IDisposable> _watchers = new UseConcurrentReliableOpsWhenCastToIDictionary<string, IDisposable>(new ConcurrentDictionary<string, IDisposable>());

        public TimeSeriesNotificationManager(IServiceCommandFactory cmdFactory, IResolveTypes typeResolver, ITimeSeriesWatcherCfg[] cfg)
        {
            _typeResolver = typeResolver;
            _cfg = cfg;
        }

        public override IObservable<CommandResult> Start(string @from = null, string options = null)
        {
            return Rxn.DfrCreate(() =>
            {
                foreach (var cfg in _cfg)
                    _watchers.AddOrReplace(cfg.Name, Watch(cfg.Stream, cfg.When, cfg.Perform));

                return CommandResult.Success("Started with {0} config entries".FormatWith(_watchers.Count));
            });
        }

        public IDisposable Watch(IObservable<TimeSeriesData> stream, Func<TimeSeriesData, bool> when, Func<TimeSeriesData, IObservable<Unit>> perform)
        {
            return stream.Where(when).SelectMany(perform).Until(OnError);
        }

        public IObservable<CommandResult> Handle(WhenReportIs command)
        {
            return Rxn.DfrCreate(() =>
            {
                if (_watchers.ContainsKey(command.Name)) return CommandResult.Failure("There is already a watcher named {0}".FormatWith(command.Name)).AsResultOf(command);

                var stream = ParseTimeSeries(command.TimeSeriesType);
                if (stream == null) return CommandResult.Failure("{0} is not a valid ITimeSeriesView".FormatWith(command.TimeSeriesType));

                var when = ParseWhen(command.WhenConditionType);
                if (when == null) return CommandResult.Failure("{0} is not a valid WhenCondition".FormatWith(command.TimeSeriesType));

                var perform = ParsePerform(command.PerformActionType);
                if (perform == null) return CommandResult.Failure("{0} is not a valid Performance".FormatWith(command.TimeSeriesType));

                _watchers.Add(command.Name, Watch(stream.GetUpdates(), when, perform));

                return CommandResult.Success("Your watcher has been created as {0}".FormatWith(command.Name));
            });
        }

        public IObservable<CommandResult> Handle(StopWhenReportIs command)
        {
            return Rxn.DfrCreate(() =>
            {
                if (!_watchers.ContainsKey(command.Name)) return CommandResult.Failure("No watchers defined for '{0}'", command.Name);

                _watchers.Remove(command.Name);
                return CommandResult.Success("Removed '{0}'".FormatWith(command.Name));
            });
        }

        private ITimeSeriesView ParseTimeSeries(string timeSeriesType)
        {
            return _typeResolver.Resolve(timeSeriesType) as ITimeSeriesView;
        }

        private Func<TimeSeriesData, IObservable<Unit>> ParsePerform(string perform)
        {
            var action = _typeResolver.Resolve(perform) as ITimeSeriesWatchAction;
            if (action == null) return null;

            return ts => action.Peform(ts);
        }

        private Func<TimeSeriesData, bool> ParseWhen(string when)
        {
            var condition = _typeResolver.Resolve(when) as ITimeSeriesWatchCondition;
            if (condition == null) return null;

            return ts => condition.When(ts);
        }

        public void ConfigiurePublishFunc(Action<IRxn> publish)
        {
            publish(new AppStatusInfoProviderEvent()
            {
                ReporterName = this.ReporterName,
                Component = "TimeSeriesNotifications",
                Info = () => new
                {
                    Watchers = _watchers.Count
                }
            });
        }

    }
}
