using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Rxns.DDD.Commanding;
using Rxns.Hosting;
using Rxns.Hosting.Cluster;
using Rxns.Interfaces;
using Rxns.Logging;


namespace Rxns
{
    /// <summary>
    /// Stops a reactor 
    /// </summary>
    public class StopReactor : ReactorCmd
    {
        public StopReactor(string name)
            : base(name)
        {
        }
    }

    /// <summary>
    /// Starts a reactor 
    /// </summary>
    public class StartReactor : ReactorCmd
    {
        public StartReactor(string name)
            : base(name)
        {
        }
    }

    /// <summary>
    /// Refreshes a reactor with events from a particluar
    /// point in time. "Replaying events"
    /// </summary>
    public class RefreshReactor : ReactorCmd
    {
        public DateTime? From { get; private set; }

        public RefreshReactor(string name, string fromDateTime = null)
            : base(name)
        {
            DateTime from;
            DateTime.TryParse(fromDateTime, out from);
            From = from;
        }
    }

    public interface IRxnFilterCfg
    {
        /// <summary>
        /// a ; seprated list of reactors to start only
        /// will always start the default reactor
        /// </summary>
        string[] IsolateReactors { get; }
    }

    public class AllowAllReactions : IRxnFilterCfg
    {
        public string[] IsolateReactors => new string[] {};
    }

    public class OnlyStartTheseReactors : IRxnFilterCfg
    {
        public OnlyStartTheseReactors(params string[] isolateReactors)
        {
            IsolateReactors = isolateReactors;
        }

        public string[] IsolateReactors { get; }
    }


    public enum RxnMode
    {
        InProcess,
        OutOfProcess,
        Cloud,
        Main,
        Supervisor
    }

    public class CaseInsensitveEqualityComparer : EqualityComparer<string>
    {
        public override bool Equals(string x, string y)
        {
            return String.Equals(x, y, StringComparison.CurrentCultureIgnoreCase);
        }

        public override int GetHashCode(string obj)
        {
            return obj.ToLower().GetHashCode();
        }
    }
    
    /// <summary>
    /// An implementation of a reactor manager targeted at an rxn sourced system. This reactor manager can start and stop reactors at your becon command,
    /// as well as historicall replay events into specific ones as the your needs dictate.
    /// </summary>
    public class ReactorManager : ReportStatusService, ICreateReactors, IManageReactors, IServiceCommandHandler<StopReactor>, IServiceCommandHandler<StartReactor>, IServiceCommandHandler<RefreshReactor>
    {
        private readonly IRxnManager<IRxn> _rxnManager;
        private readonly Func<string, IReactor<IRxn>> _reactorFactory;
        private readonly IRxnHistoryProvider _eventHistory;
        private readonly IRxnFilterCfg _cfg;
        private readonly IRxnClusterHost _cluster;
        private readonly IRxnHostableApp _app;
        private readonly IRxnCfg[] _rxnCfgs;

        public Dictionary<string, ReactorConnection> Reactors { get; } = new Dictionary<string, ReactorConnection>();
        public static string DefaultReactorName = "main"; //i want to let users configure this, so no const

        public ReactorManager(IRxnManager<IRxn> defaultReactorInputOutputStream, Func<string, IReactor<IRxn>> reactorFactory, IRxnHistoryProvider eventHistory, IRxnFilterCfg cfg, IRxnHostableApp app, IRxnCfg[] rxnCfgs, IRxnClusterHost cluster = null)
        {
            _rxnManager = defaultReactorInputOutputStream;
            _reactorFactory = reactorFactory;
            _eventHistory = eventHistory;
            _cfg = cfg;
            _cluster = cluster;
            _app = app;
            _rxnCfgs = rxnCfgs;

            //we want raw access to the eventManager, not via IrxnProcessor/IReactTo
            _rxnManager.CreateSubscription<StopReactor>().Do(r => StopReactor(r.Name)).Until().DisposedBy(this);
            _rxnManager.CreateSubscription<StartReactor>().Do(r => StartReactor(r.Name)).Until().DisposedBy(this);
            
            new DisposableAction(() =>
            {
                OnWarning("Shutting down. This usually should not happen!");
                Reactors.Values.ForEach(r =>
                {
                    r.Reactor.Dispose();
                });
            }).DisposedBy(this);
        }
        
        //todo: convert to Run syntax
        public override IObservable<CommandResult> Start(string @from = null, string options = null)
        {
            return Rxn.Create(() =>
            {
                var @default = GetOrCreate(DefaultReactorName);
                if (@default.Connection != null) return CommandResult.Success();

                @default.Connection = @default.Reactor.Chain(_rxnManager);

                return CommandResult.Success();
            });
        }


        public override IObservable<CommandResult> Stop(string @from = null)
        {
            return CommandResult.Failure("Cannot stop the reaction manager").ToObservable();
        }

        public ReactorConnection StartReactor(string reactorName, RxnMode mode = RxnMode.InProcess, IReactor<IRxn> parent = null)
        {
            OnVerbose("Locating reactor '{0}'", reactorName);
            
            var @new = GetOrCreate(reactorName);
            parent = parent ?? GetOrCreate(DefaultReactorName).Reactor;

            if (@new.Connection != null || reactorName == DefaultReactorName) return @new;
            
            mode = mode == RxnMode.OutOfProcess && !_cfg.IsolateReactors.Contains(reactorName, new CaseInsensitveEqualityComparer())
                ? RxnMode.OutOfProcess
                : RxnMode.InProcess;

            OnVerbose($"{reactorName} set to {mode}");
            
            //we are the master node
            if (mode == RxnMode.OutOfProcess)
            {
                OnInformation("Starting reactor in another process");
                @new.Connection = _cluster.SpawnOutOfProcess(_app, reactorName, @new.Reactor.Route).Until();
                return @new;
            }

            @new.Connection = parent.Chain(@new.Reactor);

            OnInformation("Successfully started '{0}'", @new.Reactor.Name);

            return @new;
        }

        public void StopReactor(string reactorName)
        {
            OnVerbose("Locating reactor '{0}'", reactorName);

            if (!Reactors.ContainsKey(reactorName)) throw new ReactorNotFound(reactorName);
            if (Reactors[reactorName].Connection == null) return; //already stopped

            var @existing = Reactors[reactorName];
            @existing.Connection.Dispose();
            @existing.Connection = null;

            OnInformation("Successfully stopped '{0}'", @existing.Reactor.Name);
        }

        private static readonly object _singleThread = new object();
        public ReactorConnection GetOrCreate(string reactorName)
        {
            lock (_singleThread)
            {
                if (Reactors.ContainsKey(reactorName)) return Reactors[reactorName];

                var reactor = new ReactorConnection()
                {
                    Reactor = _reactorFactory(reactorName)
                };
                Reactors.Add(reactorName, reactor);
           
                return reactor;
            }
        }

        public IObservable<CommandResult> Handle(StopReactor command)
        {
            return Run(() => StopReactor(command.Name));
        }

        public IObservable<CommandResult> Handle(StartReactor command)
        {
            return Run(() => StartReactor(command.Name));
        }

        public IObservable<CommandResult> Handle(RefreshReactor command)
        {
            return Run(() =>
            {
                var totalEvents = 0;
                if (!Reactors.ContainsKey(command.Name)) throw new ReactorNotFound(command.Name);

                OnVerbose("Refreshing '{0}'{1}", command.Name, command.From == null ? "" : " with events since '{0}'".FormatWith(command.From.Value));

                var reactorState = Reactors[command.Name];
                _eventHistory.GetAll(command.From).ForEach(e =>
                {
                    totalEvents++;
                    reactorState.Reactor.Input.OnNext(e);
                });

                OnInformation("Refreshed '{0}' with {1} events", command.Name, totalEvents);
            });
        }
    }
}
