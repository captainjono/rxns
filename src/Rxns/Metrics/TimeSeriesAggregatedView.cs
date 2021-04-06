using System;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Rxns.DDD.Commanding;
using Rxns.Interfaces;
using Rxns.Logging;
using Rxns.Playback;
using System.Collections.Generic;
using System.Linq;

namespace Rxns.Metrics
{
    public interface IFileSystemConfiguration
    {
        string TemporaryDirectory { get; }
    }

    /// <summary>
    /// This class is responsible for defining the view which feeds to the metrics page
    /// It is simple a collection of event aggregators of different types.
    /// not thread safe
    /// </summary>
    public abstract class TimeSeriesAggregatedView : AggregatedView<TimeSeriesData>, ITimeSeriesView
    {
        protected TimeSeriesAggregatedView(ITapeRepository reportRepo, AggViewCfg cfg) : base(reportRepo, cfg)
        {
        }

    }

    public class AggViewCfg
    {
        public string ReportDir { get; set; }
    }

    public abstract class AggregatedView<T> : ReportStatusService where T : class, IRxn
    {
        private readonly ITapeRepository _reportRepo;
        private readonly AggViewCfg _cfg;
        private IObservable<T> _stream;
        private ITapeStuff _reportTape;
        public ISubject<IRxn> Input { get; private set; }
        public ISubject<IRxn> Output { get; private set; }
        public abstract string ReportName { get; }
        private IDisposable _recording = Disposable.Empty;

        public AggregatedView(ITapeRepository reportRepo, AggViewCfg cfg)
        {
            _reportRepo = reportRepo;
            _cfg = cfg;
            Input = new Subject<IRxn>();
            Output = new Subject<IRxn>();
        }

        public override IObservable<CommandResult> Start(string @from = null, string options = null)
        {
            return Rxn.DfrCreate<CommandResult>(() =>
            {
                _reportTape = _reportRepo.GetOrCreate("{0}\\{1}".FormatWith(_cfg.ReportDir, ReportName));

                var recorder = _reportTape.Source.StartRecording();
                _recording = GetOrCreateUpdateStream().Buffer(TimeSpan.FromMilliseconds(100)).Where(b => b.Count > 0).Do(tss =>
                {
                    tss.ForEach(ts => recorder.Record(ts));
                    recorder.FlushNow();
                }).Finally(() => recorder.Dispose()).Subscribe(); //persist report

                return CommandResult.Success();
            });
        }

        public override IObservable<CommandResult> Stop(string @from = null)
        {
            return Rxn.Create(() =>
            {
                _recording.Dispose();
                return CommandResult.Success();
            });
        }

        public IObservable<T> GetHistory()
        {
            return Rxn.DfrCreate<T>(o =>
            {
                return _reportTape.Source.Contents.Where(ts => ts != null).Select(ts => ts.Recorded as T).Subscribe(o);
            });
        }

        public IObservable<T> GetUpdates()
        {
            return _stream;
        }
                

        private IObservable<T> GetOrCreateUpdateStream()
        {
            return Rxn.Create<T>(() =>
            {
                if (_stream == null)
                {
                    _stream = GetOrCreateStream().Publish().RefCount();
                }

                return _stream;
            });
        }

        public abstract IObservable<T> GetOrCreateStream();
    }
}
