using System;
using System.Collections.Generic;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Rxns.Health;
using Rxns.Interfaces;
using Rxns.Logging;
using Rxns.System.Collections.Generic;

namespace Rxns
{
    /// <summary>
    /// A reactor inspired by event sourcing.
    /// These reactors have a good reporting story, so always subscribe to there info/error channels
    /// to ensure you know whats going on.
    /// 
    /// There are two ways of working with reactors, impertively, via the IReactionCfg / IRxnProcess / IReactTo etc.
    /// interfaces and using an a support container "Reaction creator" or explictitly via newing up this class. 
    /// </summary>
    /// <typeparam name="TEvent"></typeparam>
    public class Reactor<TEvent> : ReportsStatus, IReactor<IRxn>
        where TEvent : class, IRxn
    {
        private readonly CompositeDisposable _resources = new CompositeDisposable();
        private readonly IList<object> _molecules = new List<object>();

        public IReactor<TEvent> Parent { get; private set; }
        public IEnumerable<object> Molecules { get { return _molecules; } }
        public string Name { get; set; }
        public ISubject<IRxn> Input { get; private set; }
        public ISubject<IRxn> Output { get; private set; }
        public IObservable<IReactorHealth> Health { get; private set; }

        public ISubject<IHealthEvent> Pulse { get; private set; }
        public void Shock()
        {

        }

        /// <summary>
        /// This explicitly creates a new reactor.
        /// </summary>
        /// <param name="name"></param>
        public Reactor(string name)
        {
            Setup();

            Name = name;
            _reporterName = "Rctr<{0}>".FormatWith(name);
        }

        private void Setup()
        {
            Input = new Subject<IRxn>();
            Output = new Subject<IRxn>();
            Pulse = new Subject<IHealthEvent>();
        }

        public void Stop()
        {
            if (!_molecules.AnyItems()) OnWarning("Nothing to stop");
            _resources.Dispose();
            _resources.Clear();
        }

        public IDisposable Monitor(IHealReators<IRxn> doctor) //Recovery mode
        {
            return doctor.Monitor(this);
        }

        /// <summary>
        /// Pairs this reactor with an rxn manager. The rxn manager is the parent in the relationship,
        /// anythign observed over its subscription is piped as input into the reactors world. The output 
        /// 
        /// </summary>
        /// <param name="rxnManager"></param>
        /// <param name="input"></param>
        /// <param name="output"></param>
        /// <returns></returns>
        public IDisposable Chain(IRxnManager<IRxn> rxnManager, IScheduler input = null, IScheduler output = null)
        {
            var @out = output == null ? Output : Output.ObserveOn(output);
            var @in = rxnManager.CreateSubscription<TEvent>();
            @in = input == null ? @in : @in.ObserveOn(input);

            var inChannel = @in.Subscribe(e => Input.OnNext(e), OnError);
            var outChannel = @out.Subscribe(rxnManager.Publish, OnError);
            _molecules.Add(rxnManager);

            OnInformation("Chained to '{0}'", rxnManager.GetType().Name);

            return new CompositeDisposable(inChannel, outChannel, new DisposableAction(() =>
            {
                OnInformation("Unchained '{0}'", rxnManager.GetType().Name);
                _molecules.Remove(rxnManager);
            }))
            .DisposedBy(_resources);
        }

        public IDisposable Chain<T>(IReactor<T> another) where T : IRxn
        {
            var resources = new CompositeDisposable();

            Input.Subscribe(e => another.Input.OnNext((T)e), e => ReportExceptions.OnNext(new LogMessage<Exception>() { Reporter = another.ReporterName, Level = LogLevel.Error, Message = e })).DisposedBy(resources);
            another.Output.Subscribe(r => Output.OnNext(r), e => ReportExceptions.OnNext(new LogMessage<Exception>() { Reporter = another.ReporterName, Level = LogLevel.Error, Message = e })).DisposedBy(resources);
            another.Errors.Subscribe(ReportExceptions).DisposedBy(_resources);//only report errors so we can see error state

            var health = AttachHealthMonitorIf(another);

            _molecules.Add(another);
            OnInformation("Chained to '{0}'", another.Name);

            return new CompositeDisposable(resources, new DisposableAction(() =>
            {
                if (health != null) health.Dispose();

                OnInformation("Unchained '{0}'", another.Name);
                _molecules.Remove(another);
            }))
            .DisposedBy(_resources);
        }

        public IDisposable Connect(object irxnProcessor /*recovery mode */, IScheduler inputScheduler = null)
        {
            var resource = RxnCreator.AttachProcessor(irxnProcessor, null, this, inputScheduler);
            var health = AttachHealthMonitorIf(irxnProcessor);

            _molecules.Add(irxnProcessor);
            OnInformation("Connected to rxnProcessor '{0}'", irxnProcessor.GetType().Name);

            return new CompositeDisposable(resource, new DisposableAction(() =>
            {
                if (health != null) health.Dispose();

                OnInformation("Disconnected rxnProcessor '{0}'", irxnProcessor.GetType().Name);
                _molecules.Remove(irxnProcessor);
            }))
            .DisposedBy(_resources);
        }

        public IDisposable Connect(IRxnPublisher<IRxn> publisher)
        {
            var reaction = RxnCreator.AttachPublisher(publisher, typeof(TEvent), this);
            var health = AttachHealthMonitorIf(publisher);

            _molecules.Add(publisher);
            OnInformation("Connected to EventPublisher '{0}'", publisher.GetType());

            return new CompositeDisposable(reaction, new DisposableAction(() =>
            {
                if (health != null) health.Dispose();

                OnInformation("Disconnected EventPublisher '{0}'", publisher.GetType().Name);
                _molecules.Remove(publisher);
            }))
            .DisposedBy(_resources); ;
        }

        private IDisposable AttachHealthMonitorIf(object ireportHealthOrNot)
        {
            if (_molecules.Contains(ireportHealthOrNot)) return null;

            var healthOrNot = ireportHealthOrNot as IReportHealth;
            if (healthOrNot != null)
            {
                OnInformation("Monitoring health of {0}", ireportHealthOrNot.GetType().Name);
            }

            return healthOrNot != null ? healthOrNot.ReportsWith(this) : null;
        }

        public IDisposable Connect(IReactTo<IRxn> reaction, IScheduler inputScheduler = null)
        {
            var resource = RxnCreator.AttachReaction(reaction, typeof(IRxn), this, inputScheduler);
            var health = AttachHealthMonitorIf(reaction);

            _molecules.Add(reaction);
            OnInformation("Connected to reaction '{0}'", reaction.GetType());

            return new CompositeDisposable(resource, new DisposableAction(() =>
            {
                if (health != null) health.Dispose();

                OnInformation("Disconnected reaction '{0}'", reaction.GetType());
                _molecules.Remove(reaction);
            }))
            .DisposedBy(_resources);
        }

        public override void Dispose()
        {
            Stop();
            base.Dispose();
        }
    }
}
