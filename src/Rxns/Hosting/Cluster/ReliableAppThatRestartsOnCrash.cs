using System;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Rxns.Commanding;
using Rxns.Health.AppStatus;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns.Hosting.Cluster
{
    public interface IRampupStyle
    {
        TimeSpan DownSpeed { get; }
        TimeSpan UpSpeed { get; }
        TimeSpan SampleInterval { get; }
        Func<SpareReactorAvailible, bool> ShouldScaleout { get; }

        //public Func<>
    }

    /// <summary>
    /// This rampup style
    /// </summary>
    public class ScaleoutToEverySpareReactor : IRampupStyle
    {
        public TimeSpan RampUpSpeed { get; } = TimeSpan.FromSeconds(1);

        public TimeSpan DownSpeed { get; }
        public TimeSpan UpSpeed { get; }
        public TimeSpan SampleInterval { get; }

        public Func<SpareReactorAvailible, bool> ShouldScaleout { get; } = spareConnected =>
        {
            return true;
        };
    }

    /// <summary>
    /// Define a reaction that the system will horizontially scale out
    /// </summary>
    public class AutoScaleoutReactorPlan : IRxnAppScalingPlan, IRxnProcessor<SpareReactorAvailible>
    {
        private readonly IRampupStyle _rampup;
        private readonly string _systemName;
        private readonly string _reactor;
        private readonly string _version;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="rampup">The way the system will scale out the reaction</param>
        /// <param name="systemName">The reaction to scaleout. This info is pulled from the rxn's rxnappinfo</param>
        /// <param name="version">Null indicates will scaleout with latest version and stay upto date automatically. otherwise u can still upgrade the scaleouts later with UpdateSystemCommand's</param>
        public AutoScaleoutReactorPlan(IRampupStyle rampup, string systemName, string reactor = null, string version = null)
        {
            _rampup = rampup;
            _systemName = systemName;
            _reactor = reactor;
            _version = version;
        }

        public IDisposable Monitor(string name, IRxnAppContext app)
        {
            return app.RxnManager.CreateSubscription<SpareReactorAvailible>().SelectMany(Process).SelectMany(app.RxnManager.Publish).Until();
        }

        public IObservable<IRxn> Process(SpareReactorAvailible @event)
        {
            return Rxn.Create(() =>
            {
                if (!_rampup.ShouldScaleout(@event)) return null;

                $"Updating {@event.Route}".LogDebug();
                
                return _reactor.IsNullOrWhitespace()
                    ? new UpdateSystemCommand(_systemName, _version, @event.Route)
                    : new UpdateSystemCommand(_systemName, _reactor, _version, @event.Route);
            });
        }
    }

    /// <summary>
    /// This is a reliable plan which will monitor a reaction for info messages (supports RLM) and
    /// also will restart the app if at any time it crashes
    /// </summary>
    public class ReliableAppThatRestartsOnCrash : IRxnAppScalingPlan
    {
        private readonly IRxnManager<IRxn> _rxnManager;

        public ReliableAppThatRestartsOnCrash(IRxnManager<IRxn> rxnManager)
        {
            _rxnManager = rxnManager;
        }

        public IDisposable Monitor(string name, IRxnAppContext app)
        {
            var resources = new CompositeDisposable();

            "Setting up remote logging".LogDebug();

            _rxnManager.CreateSubscription<RLM>()
                .Where(msg => msg.S == name)
                .Do(log =>
                {
                    ReportStatus.Log.OnMessage(LogLevel.None, log.ToString(), null, name);
                })
                .Until(ReportStatus.Log.OnError)
                .DisposedBy(resources);

            app.Status
                .Skip(1)
                .Where(a => a != ProcessStatus.Active)
                .SelectMany(_ =>
                {
                    //resources.DisposeAll();
                    //resources.Clear();
                    
                    return _rxnManager.Publish(new ReactorCrashed() { Name = name })
                        .Catch<Unit, Exception>(___ => new Unit().ToObservable())
                        .SelectMany(___ => Rxn.On(CurrentThreadScheduler.Instance, () => app.Start()).SelectMany(s => s));
                })
                .Do(_ => $"{name} recovered from a crash".LogDebug())
                .Until(ReportStatus.Log.OnError)
                .DisposedBy(resources);

            return resources;
        }

        private IObservable<Unit> RestartApp(IRxnAppContext app)
        {
            "todo: restart app".LogDebug();
            return new Unit().ToObservable();
        }
    }
}
