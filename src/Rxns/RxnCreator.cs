using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Rxns.DDD.Commanding;
using Rxns.DDD.CQRS;
using Rxns.Health;
using Rxns.Interfaces;


namespace Rxns
{
    /// <summary>
    /// A factory class which knows how chain molecules to reactors, estbalishing
    /// a reaction that has begun to listen to input from the outside world, and react.
    /// </summary>
    public class RxnCreator
    {
        private static readonly string[] _allRoutes = new[]
        {
            typeof(IRxnProcessor<>).Name,
            typeof(IDomainCommandHandler<,>).Name,
            typeof(IDomainQueryHandler<,>).Name,
            typeof(IReactTo<>).Name
        };

        public static Type[] DiscoverRoutes(string name, IResolveTypes container)
        {
            var reactorMolecules = container.Resolve<IRxnCfg[]>().Where(c => c.Reactor?.ToLower() == name.ToLower());
            var reactor = container.Resolve<IManageReactors>().GetOrCreate(name);

            return reactorMolecules.SelectMany(r => DiscoverRoutes(r)).Concat(reactor.Reactor.Molecules.SelectMany(r => DiscoverRoutes(r))).Distinct().ToArray();
        }

        public static Type[] DiscoverRoutes(object rxn)
        {
            var types = new List<Type>();


            
            types.Add(typeof(RxnQuestion));
            //all these types listen for events which seperate the eventstream into what is essentially topics
            //these topics map to routes


            foreach (var route in _allRoutes)
            {
                types.AddRange(rxn.GetType().GetInterfaces().Where(i => i.Name.Equals(route, StringComparison.OrdinalIgnoreCase))
                    .Select(pr => pr.GenericTypeArguments.First()).ToArray());
            }

            return types.ToArray();
        }

        public static IDisposable AttachPublisher<TEvent>(object IRxnPublisher, Type evt, IReactor<TEvent> reactor)
        {
            var p = IRxnPublisher;

            reactor.OnInformation("Configuring publisher '{0}:{1}'", p.GetType().Name, evt.Name);
            try
            {
                p.Invoke("ConfigiurePublishFunc", new object[] { new Action<TEvent>(reactor.Output.OnNext) });
            }
            catch (Exception e)
            {
                reactor.OnError( new Exception("Cannot configure publisher because of: {0}".FormatWith(e.Message)));
                return Disposable.Empty;
            }

            return new DisposableAction(() =>
            {
                var publisher = p;
                publisher.Invoke("ConfigiurePublishFunc", new object[] { new Action<TEvent>(eve => { }) });
            });
        }

        /// <summary>
        /// Attaches a reaction component to a reactor, leaving it ready to use
        /// </summary>
        /// <param name="ireactTo">The component to attach</param>
        /// <param name="evt">The base rxn the reaction should use</param>
        /// <param name="reactor">The reactor to attach the component too</param>
        /// <param name="inputScheduler">The schedule used to sent events to the reaction</param>
        /// <returns>A resource that is used to end the reaction</returns>
        public static IDisposable AttachReaction(object ireactTo, Type evt, IReactor<IRxn> reactor, IScheduler inputScheduler)
        {
            return AttachReactToImpl(ireactTo, evt, reactor, inputScheduler);
        }

        /// <summary>
        /// A default implementation of the AttachReaction method
        /// </summary>
        public static Func<object, Type, IReactor<IRxn>, IScheduler, IDisposable> AttachReactToImpl =
            (processor, evt, reactor, inputScheduler) =>
            {
                IObservable<IRxn> inputStream;
                IDeliveryScheme<IRxn> postman = null;
                var p = processor;

                var inputConfigInterface = typeof(IRxnCfg);
                var inputStreamType = typeof(IObservable<>).MakeGenericType(typeof(IRxn));

                if (p.ImplementsInterface(inputConfigInterface))
                {
                    reactor.OnVerbose("Found configuration interface'");
                    //configure input stream
                    inputStream = reactor.Input.Where(e => e.GetType().IsAssignableTo(evt));

                    var method = inputConfigInterface.GetMethod("ConfigureInput", new Type[] { inputStreamType });
                    inputStream = method.Invoke(p, new[] { inputStream }) as IObservable<IRxn>;

                    postman = p.GetProperty("InputDeliveryScheme") as IDeliveryScheme<IRxn>;
                }
                else
                {
                    inputStream = reactor.Input.Where(e => e.GetType().IsAssignableTo(evt));
                }

                if (inputScheduler != null) inputStream = inputStream.ObserveOn(inputScheduler);

                reactor.OnInformation("Establishing reaction for: {0}:{1}", p.GetType().Name, evt.Name);
                var input = processor.GetProperty("Input") as ISubject<IRxn>;
                var output = processor.GetProperty("Output") as ISubject<IRxn>;

                var inputResource = Disposable.Empty;
                var outputResource = Disposable.Empty;
                //verify null before connecting channels as null is ok in my books.
                if (input != null)
                    inputResource = postman != null
                        ? inputStream.Subscribe(e => postman.Deliver(e, t => input.OnNext(t)), reactor.OnError)
                        : inputStream.Subscribe(e => input.OnNext(e), reactor.OnError);

                if (output != null)
                    outputResource = output.Subscribe((processor as IReportStatus ?? reactor), e => reactor.Output.OnNext(e), (evnt, e) => OnError((processor as IReportStatus ?? reactor), processor, evnt, e));

                return new CompositeDisposable(inputResource, outputResource);
            };

        /// <summary>
        /// Attaches a processing component (IrxnProcessor) to a reactor, leaving it ready to use
        /// </summary>
        /// <param name="irxnProcessor">The component to attach</param>
        /// <param name="processorTypeCanBeNull">The base type used for reaction pipeline. Null defaults to the IRxn</param>
        /// <param name="reactor">The reactor to attach to</param>
        /// <param name="inputScheduler">The sheduler used for the Process method</param>
        /// <returns>A resource which ends the reaction</returns>
        public static IDisposable AttachProcessor(object irxnProcessor, Type processorTypeCanBeNull, IReactor<IRxn> reactor, IScheduler inputScheduler)
        {
            return StartProcessorImpl(irxnProcessor, processorTypeCanBeNull, reactor, inputScheduler);
        }

        /// <summary>
        /// A default implementation of the AttachProcessor method
        /// </summary>
        public static Func<object, Type, IReactor<IRxn>, IScheduler, IDisposable> StartProcessorImpl =
            (processor, processorType, reactor, inputScheduler) =>
            {

                IObservable<IRxn> inputStream = reactor.Input;
                var shouldMonitorHealth = false;
                string processorName = null;

                if (processorType != null && !processorType.IsAssignableTo<IRxn>()) throw new NotSupportedException("{0} must implement IRxn to be used with an rxnProcessor");

                var inputConfigInterface = typeof(IRxnCfg);
                var eventObs = typeof(IObservable<>).MakeGenericType(typeof(IRxn));
                IDeliveryScheme<IRxn> postman = null;

                //we try and gather all the interface here so we can efficently branch to our
                //process methods
                if (processorType == null)
                {
                    var interfaceName = typeof(IRxnProcessor<>).Name;
                    var allProcessors = processor.GetType().GetInterfaces().Where(i => i.Name.Equals(interfaceName));
                    var allProcessorTypes = allProcessors.Select(pr => pr.GenericTypeArguments.First()).ToArray();

                    reactor.Handles(allProcessorTypes);

                    if (!allProcessorTypes.AnyItems()) throw new NotSupportedException("processors must implement IRxnProcess<T :IRxn> to be connected with this method");

                    inputStream = inputStream.Where(e => allProcessorTypes.Any(evnt => e.GetType().IsAssignableTo(evnt)));

                    if (allProcessorTypes.Length > 1)
                        reactor.OnVerbose("Combining '{0}' interfaces into a single pipeline", allProcessorTypes.Length);
                    else if (allProcessorTypes.Length == 1)
                        reactor.OnVerbose("Creating single pipeline for IRxnProcessor<{0}>", allProcessorTypes.First().Name);
                }
                else
                {
                    if (processor.GetType().GetMethod("Process", new[] { processorType }) == null) throw new NotSupportedException("processors must implement IRxnProcess<{0}> to be connected with this method".FormatWith(processorType));

                    inputStream = reactor.Input.Where(e => e.GetType().IsAssignableTo(processorType));
                    reactor.OnVerbose("Creating single pipeline for IrxnProcessor<{0}>", processorType.Name);
                }

                var cfg = processor as IRxnCfg;

                if (cfg != null)
                {


                    reactor.OnVerbose("Found configuration interface");

                    inputStream = cfg.ConfigureInput(inputStream);
                    postman = cfg.InputDeliveryScheme;
                    shouldMonitorHealth = cfg.MonitorHealth;

                    var healthReporter = processor as IReportHealth;
                    if (healthReporter != null) processorName = healthReporter.ReporterName;
                }

                if (processorName.IsNullOrWhitespace()) processorName = processor.GetType().Name;


                Func<IRxn, IObservable<IRxn>> doDelivery;
                if (postman != null)
                    doDelivery = evnt => postman.Deliver(evnt, e => processor.Invoke("Process", new object[] { e }) as IObservable<IRxn>);
                else
                    doDelivery = e => processor.Invoke("Process", new object[] { e }) as IObservable<IRxn>; //p.Invoke("Process", new object[] {e}) as IObservable<IRxn>;

                //todo: make integration neater
                if (true)
                {
                    var health = HealthMonitor.ForQueue<IRxn>(reactor, processorName);
                    inputStream = inputStream.Monitor(health.Select(h => h.Before()).ToArray());

                    //now hook everything up
                    if (inputScheduler != null) inputStream = inputStream.ObserveOn(inputScheduler);

                    return inputStream.Subscribe(processor as IReportStatus ?? reactor, evnt =>
                    {
                        try
                        {
                            var result = doDelivery(evnt);

                            //publish all events that resulted from the processing

                            if (result != null)
                                result
                                    .MonitorR(health.Select(h => h.After()).ToArray())
                                    .Subscribe(reactor.Output.OnNext, error => OnError((processor as IReportStatus ?? reactor), processor, evnt, error));
                        }
                        catch (Exception e)
                        {
                            foreach (var trg in health.Select(h => h.After()).ToArray())
                                if (trg.When(evnt)) trg.Do(evnt);

                            OnError((processor as IReportStatus ?? reactor), processor, evnt, e);
                        }
                    });
                }
                else
                {
                    //now hook everything up
                    if (inputScheduler != null) inputStream = inputStream.ObserveOn(inputScheduler);

                    return inputStream.Subscribe(processor as IReportStatus ?? reactor, evnt =>
                    {
                        var result = doDelivery(evnt);

                        //publish all events that resulted from the processing
                        if (result != null)
                            result.Subscribe(reactor.Output.OnNext, error => OnError((processor as IReportStatus ?? reactor), processor, evnt, error));
                    });
                }
            };

        /// <summary>
        /// Called each time an error occours during a reaction. Override this static method if you wish to override the default behaviour
        /// of OnErroring the reactor or the processor (if it supports IReportStatus)
        /// </summary>
        public static Action<IReportStatus, object, IRxn, Exception> OnError = (reporter, impl, evnt, error) =>
        {
            reporter.OnError(new ReactionException(error, "[{0}] On {1}{2} -> {3}", impl.GetType().Name, evnt.GetType().Name, PrettyPrint(evnt), error.Message));
        };

        private static object PrettyPrint(IRxn obj)
        {
            return obj.GetType().GetProperties().Select(p => "{0}:{1}".FormatWith(p.Name, p.CanRead ? p.GetValue(obj) : "(na)")).ToStringEach();
        }

        /// <summary>
        /// A class is able to specificy what reactor it should be hooked up to by defining the IRxnCfg class. If none is specified
        /// the default reactor is provided.
        /// </summary>
        /// <param name="processorPublisherReaction">The class to query for a reaction config</param>
        /// <param name="getReactorByName">The factory function that know hows to get a reactor by its name</param>
        /// <returns>The reactor as specified by the class</returns>
        public static IReactor<IRxn> GetReactorFor(object processorPublisherReaction, Func<string, IReactor<IRxn>> getReactorByName)
        {
            var inputConfigInterface = typeof(IRxnCfg);

            if (processorPublisherReaction.ImplementsInterface(inputConfigInterface))
            {
                var name = processorPublisherReaction.GetProperty("Reactor") as string ?? ReactorManager.DefaultReactorName;
                return getReactorByName(name);
            }

            return getReactorByName(ReactorManager.DefaultReactorName);
        }

        public static bool? IsIsolatedProcess(IRxnFilterCfg cfg, string reactorName)
        {
            if (reactorName == ReactorManager.DefaultReactorName) return null;
            if (!cfg.IsolateReactors.AnyItems()) return null;

            return cfg.IsolateReactors.Contains(reactorName, new CaseInsensitveEqualityComparer());
        }
        public static Func<string, RxnMode, IResolveTypes, IReactor<IRxn>> getReactorByName = (reactorName, cfg, con) => con.Resolve<IManageReactors>().StartReactor(reactorName, cfg).Reactor;

        public static IDisposable StartRxnPublishers(IReportStatus logger, IResolveTypes container, Type[] events)
        {
            var startedProcessors = new List<dynamic>();
            var resources = new CompositeDisposable();
            var cfg = container.Resolve<IRxnFilterCfg>();

            foreach (var @event in events.EmptyIfNull())
            {
                try
                {
                    //now we create an rxnProcessor of each IRxn discovered, then ask the container
                    //for any interfaces which implement them.
                    var processorForEvent = typeof(IRxnPublisher<>).MakeGenericType(@event);
                    var allProcessorsForEventType = typeof(IEnumerable<>).MakeGenericType(processorForEvent);
                    var allProcessors = (IEnumerable<dynamic>)container.Resolve(allProcessorsForEventType);

                    var distinctProcessors = allProcessors.Distinct().ToArray();
                    //now register any processors found with the subscription to the events they are interested in
                    foreach (var publisher in distinctProcessors)
                    {
                        var pcfg = publisher as IRxnCfg;

                        var mode = pcfg?.Mode ?? RxnMode.InProcess;
                        var reactor = RxnCreator.GetReactorFor(publisher as object, name => getReactorByName(name, mode, container));

                        var isOutOfProcess = mode == RxnMode.OutOfProcess;
                        var outOfProcessReactor = pcfg?.Reactor;

                        var isolationState = IsIsolatedProcess(cfg, reactor.Name);
                        if (isolationState == null)
                        {
                            //normal
                        }
                        else if (!isolationState.Value)
                        {
                            logger.OnVerbose($"Bypassing '{publisher.GetType().Name}' due to isolation mode being active {cfg.IsolateReactors.ToStringEach()}");
                            return Disposable.Empty;
                        }

                        if (isOutOfProcess && outOfProcessReactor.IsNullOrWhitespace())
                        {
                            throw new Exception("Cant out of process on default reactor");
                        }
                        else if (isOutOfProcess)
                        {

                            IDisposableExtensions.DisposedBy(reactor.Connect(publisher), resources);

                            var startupReactor = getReactorByName(pcfg.Reactor, RxnMode.OutOfProcess, container);
                            return Disposable.Empty;
                        }

                        IDisposableExtensions.DisposedBy(reactor.Connect(publisher), resources);
                    }
                    startedProcessors.AddRange(distinctProcessors); //so we need to keep a list processors we see so we dont attach them twice

                }
                catch (Exception e)
                {
                    logger.OnError(e);
                }
            }

            return resources;
        }

        public static IDisposable StartReactions(IReportStatus logger, IResolveTypes container, Type[] events, IScheduler eventDelivery)
        {
            var reactors = new CompositeDisposable();
            var cfg = container.Resolve<IRxnFilterCfg>();

            foreach (var @event in events.EmptyIfNull())
            {
                try
                {
                    var processorForEvent = typeof(IReactTo<>).MakeGenericType(@event);
                    var allProcessorsForEventType = typeof(IEnumerable<>).MakeGenericType(processorForEvent);
                    var allProcessors = (IEnumerable<dynamic>)container.Resolve(allProcessorsForEventType);

                    //now attach any processors found with the subscription to the events they are interested in
                    foreach (object reaction in allProcessors.Distinct())
                    {
                        var pcfg = reaction as IRxnCfg;

                        var mode = pcfg?.Mode ?? RxnMode.InProcess;
                        var isOutOfProcess = mode == RxnMode.OutOfProcess;
                        var outOfProcessReactor = pcfg?.Reactor;

                        var reactor = RxnCreator.GetReactorFor(reaction, name => getReactorByName(name, mode, container));

                        var isolationState = IsIsolatedProcess(cfg, reactor.Name);
                        if (isolationState == null)
                        {
                            //normal
                        }
                        else if (!isolationState.Value)
                        {
                            logger.OnVerbose($"Bypassing '{reaction.GetType().Name}' due to isolation mode being active {cfg.IsolateReactors.ToStringEach()}");
                            return Disposable.Empty;
                        }

                        if (isOutOfProcess && outOfProcessReactor.IsNullOrWhitespace())
                        {
                            throw new Exception("Cant out of process on default reactor");
                        }
                        else if (isOutOfProcess)
                        {
                            reactor.Handles(reaction.GetType().GenericTypeArguments.First());

                            return getReactorByName(outOfProcessReactor, RxnMode.OutOfProcess, container);
                        }

                        IDisposableExtensions.DisposedBy(reactor.Connect((IReactTo<IRxn>)reaction, eventDelivery), reactors);

                        reactors.Add(reactor);
                    }
                }
                catch (Exception e)
                {
                    logger.OnError(e);
                }
            }

            return reactors;
        }

        public static IDisposable StartRxnProcessors(IReportStatus logger, IResolveTypes container, Type[] events, IScheduler eventDelivery)
        {
            var startedProcessors = new List<dynamic>();
            var resources = new CompositeDisposable();
            var cfg = container.Resolve<IRxnFilterCfg>();

            foreach (var @event in events.EmptyIfNull().Concat(new[] { typeof(IDomainCommand), typeof(IDomainQuery) }))
            {
                try
                {
                    //now we create an rxnProcessor of each IRxn discovered, then ask the container
                    //for any interfaces which implement them.
                    var processorForEvent = typeof(IRxnProcessor<>).MakeGenericType(@event);
                    var allProcessorsForEventType = typeof(IEnumerable<>).MakeGenericType(processorForEvent);
                    var allProcessors = ((IEnumerable<dynamic>)container.Resolve(allProcessorsForEventType)).ToArray();

                    //now register any processors found with the subscription to the events they are interested in
                    var distinctProcessors = allProcessors.Distinct().ToArray();
                    foreach (var p in distinctProcessors.Except(startedProcessors))
                    {
                        var pcfg = p as IRxnCfg;
                        var outOfProcessReactor = pcfg?.Reactor;

                        var reactor = RxnCreator.GetReactorFor((object)p, name => getReactorByName(name, pcfg?.Mode ?? RxnMode.InProcess, container));


                        var isolationState = IsIsolatedProcess(cfg, reactor.Name);
                        var isOutOfProcess = (pcfg?.Mode == RxnMode.OutOfProcess) && (isolationState.HasValue && !isolationState.Value);

                        if (isolationState == null)
                        {
                            //normal
                        }
                        else if (!isolationState.Value)
                        {
                            logger.OnVerbose($"Bypassing '{p.GetType().Name}' due to isolation mode being active {cfg.IsolateReactors.ToStringEach()}");

                            return Disposable.Empty;
                        }

                        if (isOutOfProcess && outOfProcessReactor.IsNullOrWhitespace())
                        {
                            throw new Exception("Cant out of process on default reactor");
                        }
                        else if (isOutOfProcess)
                        {
                            var startupReactor = getReactorByName(pcfg?.Reactor, RxnMode.OutOfProcess, container);
                            return Disposable.Empty;
                        }

                        logger.OnInformation($"Connecting {reactor.Name}");

                        //this method will hookup ALL process methods of a processor in one call
                        IDisposableExtensions.DisposedBy(reactor.Connect((object)p, eventDelivery), resources);

                    }

                    startedProcessors.AddRange(distinctProcessors); //so we need to keep a list processors we see so we dont attach them twice
                }
                catch (Exception e)
                {
                    logger.OnError(e);
                }
            }

            return resources;
        }

    }
}
