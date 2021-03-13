using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Rxns.Commanding;
using Rxns.DDD;
using Rxns.DDD.Commanding;
using Rxns.DDD.CQRS;
using Rxns.Health.AppStatus;
using Rxns.Hosting.Cluster;
using Rxns.Hosting.Updates;
using Rxns.Interfaces;
using Rxns.Logging;
using Rxns.Microservices;
using Rxns.NewtonsoftJson;

namespace Rxns.Hosting
{
    public class RxnManagerClusterClient : ReportsStatus, IRxnClusterHost, IRxnAppContext
    {
        private readonly string[] _args;
        private readonly IRxnManager<IRxn> _rxns;

        ISubject<ProcessStatus> _status = new BehaviorSubject<ProcessStatus>(ProcessStatus.Active);
        public RxnManagerClusterClient(IRxnManager<IRxn> rxns, string[] args)
        {
            _args = args;
            RxnManager = _rxns = rxns;
        }

        public string[] args => _args;
        public IObservable<IRxnAppContext> Start(bool shouldStartRxns = true, IAppContainer container = null)
        {
            _status.OnNext(ProcessStatus.Active);
            
            return this.ToObservable();
        }

        public void Terminate()
        {
            
        }

        public IAppSetup Installer { get; }
        public ICommandService CmdService { get; }
        public IAppCommandService AppCmdService { get; }
        public IRxnManager<IRxn> RxnManager { get; }
        public IResolveTypes Resolver { get; }
        public IObservable<ProcessStatus> Status => _status;
        public IRxnHostableApp App { get; }

        IDisposable IRxnHost.Start()
        {
            return _rxns.Activate().Until(OnError);
        }

        public void Restart(string version = null)
        {
            
        }

        public IObservable<Unit> Install(string installer, string version)
        {
            return new Unit().ToObservable();
        }

        public IObservable<IRxnHostReadyToRun> Stage(IRxnHostableApp app, IRxnAppCfg cfg)
        {
            throw new NotImplementedException();
        }

        public string Name { get; set; } = "RemoteClusterClient";
        public IList<IRxnAppContext> Apps { get; }
        public IRxnClusterHost ConfigureWith(IRxnAppScalingManager scalingManager)
        {
            return this;
        }

        public IObservable<IRxnAppContext> SpawnOutOfProcess(IRxnHostableApp rxn, string name, Type[] routes)
        {
            return _rxns.Publish(new SendReactorOutOfProcess(name, routes)).Select(_ => this);
        }


    }

    public class ClusteredAppInfo : IRxnAppInfo
    {
        public string Name { get; set; }
        public string Version { get; set; }
        public string Url { get; set;  }
        public string Id { get; }
        public bool KeepUpdated { get; }

        public ClusteredAppInfo(string name, string version, string[] args, bool keepUpdated)
        {
            KeepUpdated = keepUpdated;
            Name = name;
            Version = version;

            var reactor = args.SkipWhile(a => a != "reactor").Skip(1).FirstOrDefault();
            if (!reactor.IsNullOrWhitespace())
            {
                Name = $"{Name}[{reactor}]";
            }

        }
    }


    public class ClusteredAppHost : ReportsStatus, IRxnClusterHost, IRxnHostReadyToRun, IServiceCommandHandler<PrepareForAppUpdate>, IServiceCommandHandler<MigrateAppToVersion>, IServiceCommandHandler<GetAppDirectoryForAppUpdate>
    {
        private readonly IRxnAppProcessFactory _processFactory;
        private readonly IRxnManager<IRxn> _hostEventLoop;
        private readonly RoutableBackingChannel<IRxn> _router;
        private readonly IRxnHostManager _hostManager;
        private readonly IRxnAppCfg _cfg;
        private readonly IStoreAppUpdates _appStore;
        private ClusteringWorkflow _cluster;
        private bool _isReactorProcess;
        private IRxnHostableApp _app;

        public IList<IRxnAppContext> Apps => _cluster.Apps.Values.ToList();

        public string Name { get; set; } = "ClustedAppHost";

        public ClusteredAppHost(IRxnAppProcessFactory processFactory, IRxnManager<IRxn> hostEventLoop, RoutableBackingChannel<IRxn> router, IRxnHostManager hostManager, IRxnAppCfg cfg, IStoreAppUpdates appStore)
        {
            _processFactory = processFactory;
            _hostEventLoop = hostEventLoop;
            _router = router ?? new RoutableBackingChannel<IRxn>(new LocalOnlyRegistry(new RxnManager<IRxn>(new LocalBackingChannel<IRxn>())));
            _hostManager = hostManager;

            if(!cfg.Args.Any(a => a.Equals("reactor")))
                _hostEventLoop
                    .CreateSubscription<SendReactorOutOfProcess>()
                    .Do(reactor =>
                    {
                        //this is called when attempting to spawn another reactor from a subprocess
                        SpawnOutOfProcess(Apps.FirstOrDefault().App, reactor.Name, reactor.Routes).Until(OnError);

                    })
                    .Until()
                    .DisposedBy(this);
            
            _cfg = cfg;
            _appStore = appStore;
        }

        public IRxnClusterHost ConfigureWith(IRxnAppScalingManager scalingManager)
        {
            _cluster = new ClusteringWorkflow(_processFactory, _hostManager, scalingManager, _cfg, _hostEventLoop);

            return this;
        }
        
        public IDisposable Start()
        {
            if (_cfg.Args.Contains("reactor"))
            {
                _isReactorProcess = true;
                //indicates a reactor is starting out of process
                return Disposable.Empty;
            }
            else //this is the normal startup of the app. boot as usual
            {

                return Disposable.Empty;
            }
        }

        public IObservable<IRxnHostReadyToRun> Stage(IRxnHostableApp app, IRxnAppCfg cfg)
        {
            return Rxn.Create<IRxnHostReadyToRun>(() =>
            {
                _app = app;

                app.Definition.UpdateWith(def =>
                {
                    def.CreatesOncePerApp(_ => _hostManager);
                    def.CreatesOncePerApp(_ => _processFactory);
                    //def.CreatesOncePerApp(_ => cfg);
                    def.CreatesOncePerApp(_ => app);
                    def.CreatesOncePerApp(_ => cfg);
                    def.CreatesOncePerApp(_ => this);
                    def.CreatesOncePerApp(_ => new SupervisorAppUpdateProvider(_.Resolve<ICommandService>()));
                });

                return this;
            });
        }

        public IObservable<IRxnAppContext> Run(IAppContainer container = null)
        {
            return Rxn.Create<IRxnAppContext>(o =>
            {
                $"Starting app: {_app.AppInfo.Name}:{_app.AppInfo.Version}".LogDebug();

                //todo: use a rxncreator method to perform this hookup
                //setup system updates in supervisor
                _hostEventLoop.CreateSubscription<UpdateSystemCommand>()
                    .SelectMany(updateCmd => ((IAppUpdateManager)_app.Resolver.ResolveOptional(typeof(IAppUpdateManager)))?.Handle(updateCmd) ?? Rxn.Empty<IRxn>())
                    .SelectMany(_ => _hostEventLoop.Publish(_)).Until();

                //for migrate cmd
                _hostEventLoop.CreateSubscription<RxnQuestion>()
                    .Where(e => e.Options.Contains("Migrate")) //todo: fix routes such that this is not required
                    .SelectMany(updateCmd => _app.Resolver.Resolve<ServiceCommandExecutor>().Process(updateCmd))
                    .SelectMany(_ => _hostEventLoop.Publish(_)).Until();

                return _cluster.Run(_app, _cfg).Subscribe(o);
            });
        }


        public IObservable<IRxnAppContext> SpawnOutOfProcess(IRxnHostableApp rxn, string name, Type[] routes)
        {
            return _cluster.SpawnReactor(rxn, name, routes);
        }
        
        public void Restart(string version = null)
        {
            "Restart on clusterhost not implemented yet".LogDebug();
        }


        public IObservable<Unit> Install(string installerZip, string version)
        {
            "Install on clusterhost not implemented yet".LogDebug();

            return new Unit().ToObservable();
        }

        public IObservable<CommandResult> Handle(GetAppDirectoryForAppUpdate command)
        {
            return _appStore.Run(command).Select(r => CommandResult.Success(r).AsResultOf(command));
        }

        public IObservable<CommandResult> Handle(PrepareForAppUpdate command)
        {
            return _appStore.Run(command).Select(r => CommandResult.Success(r).AsResultOf(command));
        }

        public IObservable<CommandResult> Handle(MigrateAppToVersion command)
        {
            return Rxn.Create(() =>
            {
                //stop all apps
                Apps.ForEach(a => a.Dispose());

                var currentCfg = RxnAppCfg.Detect(_cfg.Args);

                currentCfg.SystemName = command.SystemName;
                currentCfg.Version = command.Version;
                currentCfg.Save();

                //restartApps
                Apps.ForEach(a => a.Start(true /* this could be completely wrong? need to switch on rxn type? */).Until());

                return CommandResult.Success();
            });
        }
    }

    public class SendReactorOutOfProcess : ServiceCommand
    {
        public string Name { get; set; }
        public Type[] Routes { get; }

        public SendReactorOutOfProcess(string name, Type[] routes)
        {
            Name = name;
            Routes = routes;
        }
    }


    public class AppHostRoute
    {
        public IRxnHost Host { get; }
        public Func<IRxnApp, bool> DoesHost { get; }

        public AppHostRoute(IRxnHost host, Func<IRxnApp, bool> doesHost)
        {
            Host = host;
            DoesHost = doesHost;
        }
    }
}

