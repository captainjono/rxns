using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Rxns.DDD;
using Rxns.DDD.Commanding;
using Rxns.DDD.CQRS;
using Rxns.Hosting.Cluster;
using Rxns.Interfaces;
using Rxns.Logging;
using Rxns.NewtonsoftJson;

namespace Rxns.Hosting
{
    /*
//cluster starts up and loads up its capacity config
//can program the expressions that trigger different things
//all programs have an inital capacity of 1 - which spawns a single process
* then scale units can be applied to each reactions, which configures the bias in the reactions
-rxnClusterConfig
    -single process 
    -single machine (multi-process mode that auto-scales based on units and spareReactors)
    -cloud process
-cloud processes will scale at using a stratergy that is the same which is used for single machine
    -can code that using using a orchestrator which takes can "add process to cluster" in the same way, cloud or multi-process
        -routing is setup the same way, using a serviceregistry and a routing table
            -semantics of the reactor are availble in the creator, so we known ahead of time what events we need to repeat into that process
* that will scale out to a different process to stop backpressure building up (ms performance)
-scaleOutStratergy
  -isolated process
  -cloud process
        -function app

*/
    public static class RxnCfgExt
    {
        public static T Save<T>(this T cfg, string location = null) where T : IRxnAppCfg
        {
            File.WriteAllText(location != null ? Path.Combine(location, RxnAppCfg.CfgName) : RxnAppCfg.CfgName, cfg.ToJson());

            return cfg;
        }
    }

    public class RxnAppCfg : IRxnAppCfg
    {
        public string[] Args { get; set; } = new string[0];
        public string Version { get; set; }
        public string AppPath { get; set; }

        public static string CfgName { get; } = "rxn.cfg";
        public string SystemName { get; set; }
        public bool KeepUpdated { get; set; } = true;
        public string AppStatusUrl { get; set; }

        public static RxnAppCfg Detect(string[] args, string cfgName = null)
        {
            var cfg = LoadCfg(cfgName?? CfgName);

            cfg.Args = args;

            return cfg;
        }

        

        public static RxnAppCfg LoadCfg(string cfgFile)
        {
            if (File.Exists(cfgFile))
            {
                return File.ReadAllText(cfgFile).FromJson<RxnAppCfg>();
            }

            return new RxnAppCfg();
        }
    }

    public interface IRxnAppCfg
    {
        string[] Args { get; }

        string Version { get; set; }

        string AppPath { get; }
        string SystemName { get; set; }
        bool KeepUpdated { get; }
        string AppStatusUrl { get; }
    }

    public interface IRxnClusterHost : IRxnHost
    {
        IList<IRxnAppContext> Apps { get; }
        IRxnClusterHost ConfigureWith(IRxnAppScalingManager scalingManager);
        IObservable<IRxnAppContext> SpawnOutOfProcess(IRxnHostableApp rxn, string name, Type[] routes = null);
    }

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
        public IObservable<IRxnAppContext> Start()
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

        public IObservable<IRxnAppContext> Run(IRxnHostableApp app, IRxnAppCfg cfg)
        {
            return this.ToObservable();
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


    public class ClusteredAppHost : ReportsStatus, IRxnClusterHost, IServiceCommandHandler<GetOrCreateAppVersionTargetPath>, IServiceCommandHandler<MigrateAppToVersion>
    {
        private readonly IRxnAppProcessFactory _processFactory;
        private readonly IRxnManager<IRxn> _hostEventLoop;
        private readonly RoutableBackingChannel<IRxn> _router;
        private readonly IRxnHostManager _hostManager;
        private readonly IRxnAppCfg _cfg;
        private ClusteringWorkflow _cluster;
        private bool _isReactorProcess;

        public IList<IRxnAppContext> Apps => _cluster.Apps.Values.ToList();

        public string Name { get; set; } = "ClustedAppHost";
        
        public ClusteredAppHost(IRxnAppProcessFactory processFactory, IRxnManager<IRxn> hostEventLoop, RoutableBackingChannel<IRxn> router, IRxnHostManager hostManager, IRxnAppCfg cfg)
        {
            _processFactory = processFactory;
            _hostEventLoop = hostEventLoop;
            _router = router ?? new RoutableBackingChannel<IRxn>();
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

        public IObservable<IRxnAppContext> Run(IRxnHostableApp app, IRxnAppCfg cfg)
        {
            return Rxn.Create<IRxnAppContext>(o =>
            {
                app.Definition.UpdateWith(def =>
                {
                    def.CreatesOncePerApp(_ => _hostManager);
                    def.CreatesOncePerApp(_ => _processFactory);
                    //def.CreatesOncePerApp(_ => cfg);
                    def.CreatesOncePerApp(_ => app);
                    def.CreatesOncePerApp(_ => cfg);
                    def.CreatesOncePerApp(_ => this);
                });

                $"Starting app: {app.AppInfo.Name}:{app.AppInfo.Version}".LogDebug();
                    
                return _cluster.Run(app, cfg).Subscribe(o);
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

        public IObservable<CommandResult> Handle(GetOrCreateAppVersionTargetPath command)
        {
            return Rxn.Create(() =>
            {
                var targetPath = Path.Combine(Directory.GetCurrentDirectory(), $"{command.SystemName}%%{command.Version}");

                if (!Directory.Exists(targetPath))
                    Directory.CreateDirectory(targetPath);

                return CommandResult.Success().AsResultOf(command);
            });
        }

        public IObservable<CommandResult> Handle(MigrateAppToVersion command)
        {
            return Rxn.Create<CommandResult>(o =>
            {
                var currentCfg = RxnAppCfg.Detect(new string[0]); //bypass commandline


                if (currentCfg.SystemName.BasicallyEquals(command.SystemName) && currentCfg.Version.BasicallyEquals(command.Version))
                {
                    "Bypassing since app is already at this version".LogDebug();
                    return CommandResult.Success().AsResultOf(command).ToObservable().Subscribe(o);
                }

                var appBinary = new FileInfo(currentCfg.AppPath).Name;

                return Handle(new GetOrCreateAppVersionTargetPath(command.SystemName, command.Version)).Select(targetDir =>
                {
                    currentCfg.AppPath = Path.Combine(targetDir.Message, appBinary);
                    currentCfg.Version = command.Version;

                    currentCfg
                        .Save(targetDir.Message) //update apps cfg
                        .Save(); //update the default cfg for the supervisor so it launches that new version

                    return CommandResult.Success().AsResultOf(command);
                })
                .Subscribe(o);
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

