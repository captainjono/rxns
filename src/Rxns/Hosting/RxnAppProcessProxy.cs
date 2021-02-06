using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Rxns.DDD;
using Rxns.DDD.Commanding;
using Rxns.DDD.CQRS;
using Rxns.Health;
using Rxns.Health.AppStatus;
using Rxns.Hosting.Cluster;
using Rxns.Interfaces;
using Rxns.Logging;
using Rxns.Playback;

namespace Rxns.Hosting
{
    public enum ProcessStatus
    {
        Active,
        Killed,
        Terminated
    }

    public class AppProcessStarted : HealthEvent
    {

    }

    public class AppProcessEnded : HealthEvent
    {

    }

    public interface IRxnAppProcessContext
    {
        string[] args { get; } //todo: convert to IRxnCfg class ? how to abstract platform args away but still pass through?
        IObservable<IRxnAppContext> Start();
        void Terminate();

        IObservable<ProcessStatus> Status { get; }
    }


    public interface IRxnAppProcessFactory
    {
        IObservable<IRxnAppContext> Create(IRxnHostableApp app, IRxnHostManager hostManager, string reactorName, RxnMode mode = RxnMode.InProcess);
        IObservable<IRxnAppContext> Create(IRxnHostableApp app, IRxnHostManager hostManager, string[] args = null, RxnMode mode = RxnMode.InProcess);
        RxnMode DetectMode(IRxnAppCfg original);
    }

    public class OutOfProcessFactory : IRxnAppProcessFactory
    {
        public static RxnManager<IRxn> RxnManager { get; set; }

        
        
        public static NamedPipesServerBackingChannel CreateNamedPipeServer()
        {
            PipeServer = new NamedPipesServerBackingChannel(4);
            
            RxnManager = new RxnManager<IRxn>(PipeServer);
            RxnManager.ReportToDebug();
            RxnManager.Activate().Until();

            return PipeServer;
        }

        public static NamedPipesServerBackingChannel PipeServer { get; set; }

        public static NamedPipesClientBackingChannel CreateNamedPipeClient(string name)
        {
            var server = new NamedPipesClientBackingChannel(NamedPipesServerBackingChannel.PipeName, name);
            RxnManager = new RxnManager<IRxn>(server);
            RxnManager.ReportToDebug();
            RxnManager.Activate().Until();

            return server;
        }
        
        public void Setup(IRxnHostableApp app, string reactorName, RxnMode mode)
        {
            switch (mode)
            {
                case RxnMode.Supervisor:
                    app.Definition.UpdateWith(def =>
                    {
                        def.CreatesOncePerApp(c => new RxnManagerCommandService(RxnManager, c.Resolve<ICommandFactory>(), c.Resolve<IServiceCommandFactory>()));
                    });
                    return;
                case RxnMode.InProcess:
                case RxnMode.OutOfProcess:
                case RxnMode.Main:
                    {
                        app.Definition.UpdateWith(def =>
                        {
                            def.Includes<AppStatusServerModule>()

                            .CreatesOncePerApp(_ => RxnManager)
                            .CreatesOncePerApp<RxnAppClusterManager>()

                            .CreatesOncePerApp<InMemoryTapeRepo>(true)
                            .CreatesOncePerApp<InMemoryAppStatusStore>()
                            .Includes<DDDServerModule>()
                            .CreatesOncePerApp(_ => new RxnLogger(i =>
                            {
                                Debug.WriteLine(i);
                                RxnManager.Publish(i.ToRxn(reactorName)).Subscribe();
                            }, e =>
                            {
                                Debug.WriteLine(e);
                                RxnManager.Publish(e.ToRxn(reactorName)).Subscribe();
                            }));
                        });
                        break;
                    }
                default:

                    break;
            }

            if (reactorName != null && reactorName != "main")
            {
                app.Definition.UpdateWith(def => def.CreatesOncePerApp(_ => new OnlyStartTheseReactors(reactorName)));
            }
        }

        public RxnMode DetectMode(IRxnAppCfg original)
        {
            var hasSuperVirsor = original.Args.AnyItems();

            if (!hasSuperVirsor)
            {
                return RxnMode.Supervisor;
            }

            if (original.Args.Contains("main"))
            {
                return RxnMode.Main;
            }

            return RxnMode.InProcess;
        }

        public IObservable<IRxnAppContext> Create(IRxnHostableApp app, IRxnHostManager hostManager, string reactorName, RxnMode mode = RxnMode.InProcess)
        {
            if (reactorName.IsNullOrWhitespace())
            {
                throw new Exception("Cant create null reactor");
            }

            var args = $"reactor {reactorName}".Split(' ');

            return Rxn.Create<IRxnAppContext>(o =>
            {
                Setup(app, reactorName, mode);

                switch (mode)
                {
                    //should also then startup the supervisor host!
                    case RxnMode.OutOfProcess:
                        //var reactorName = args.Reverse().FirstOrDefault();
                        var routes = RxnCreator.DiscoverRoutes(reactorName, app.Resolver);
                        PipeServer.ListenForNewClient(reactorName, routes);
                        return new ExternalProcessRxnAppContext(app, args, RxnManager).ToObservable().Subscribe(o);
                    case RxnMode.InProcess:
                        return hostManager.GetHostForApp(reactorName).Run(app, new RxnAppCfg() { Args = args }).Subscribe(o);
                    default:
                        "cant dertermine reactor mode, defaulting to inprocess".LogDebug();
                        return hostManager.GetHostForApp(reactorName).Run(app, new RxnAppCfg() { Args = args }).Subscribe(o);
                }
            });
        }

        public IObservable<IRxnAppContext> Create(IRxnHostableApp app, IRxnHostManager hostManager, string[] args, RxnMode mode = RxnMode.InProcess)
        {

            return Rxn.Create<IRxnAppContext>(o =>
            {
                Setup(app, args.AnyItems() && args.Contains("reactor") ? args[1] : null, mode);

                switch (mode)
                {
                    case RxnMode.Supervisor:

                        // need to build up routing table from the reactor defs here
                        // the consolehost does the building so something similiar to a host
                        // syntax here, but we just want a <reactorName, type[] routes> RxnCreate.LookupReactorRoutes(RxnApp)?
                        PipeServer.ListenForNewClient("main", new Type[] { typeof(IRxn) });
                        return new ExternalProcessRxnAppContext(app, "reactor main".Split(), RxnManager).ToObservable().Subscribe(o);
                    //should also then startup the supervisor host!
                    case RxnMode.OutOfProcess:
                        var reactorName = args.Reverse().FirstOrDefault();
                        var routes = app.Resolver.Resolve<IManageReactors>().GetOrCreate(reactorName).Reactor.Molecules.SelectMany(m => RxnCreator.DiscoverRoutes(m)).ToArray();
                        PipeServer.ListenForNewClient(reactorName, routes);

                        return new ExternalProcessRxnAppContext(app, args, RxnManager).ToObservable().Subscribe(o);
                    case RxnMode.InProcess:
                        return hostManager.GetHostForApp(null).Run(app, new RxnAppCfg() { Args = args }).Subscribe(o);
                    case RxnMode.Main:
                        return hostManager.GetHostForApp("main").Run(app, new RxnAppCfg() { Args = args }).Subscribe(o);
                    default:
                        "cant dertermine mode, defaulting to inprocess".LogDebug();
                        return hostManager.GetHostForApp(null).Run(app, new RxnAppCfg() { Args = args }).Subscribe(o);
                }
            });
        }

    }

    public class OutOfProcessRxnAppContext : IRxnAppContext
    {
        private readonly string _name;
        public string[] args { get; set; }

        private readonly ReplaySubject<ProcessStatus> _status = new ReplaySubject<ProcessStatus>();
        private readonly Process _process;
        private ProcessStatus _processStatus;
        private CompositeDisposable _resources = new CompositeDisposable();
        public IAppSetup Installer { get; }
        public ICommandService CmdService => Resolver.Resolve<ICommandService>();
        public IAppCommandService AppCmdService => Resolver.Resolve<IAppCommandService>();
        public IRxnManager<IRxn> RxnManager { get; }
        public IResolveTypes Resolver => App.Resolver;
        public IObservable<ProcessStatus> Status => _status;
        public IRxnHostableApp App { get; }

        public OutOfProcessRxnAppContext(IRxnHostableApp app, IRxnManager<IRxn> rxnManager, string[] args)
        {
            App = app;
            _name = app.AppInfo.Name;
            RxnManager = rxnManager;
            this.args = args;

            if (args.Contains("reactor"))
            {
                _name = $"{_name}[{args.SkipWhile(a => a != "reactor").Skip(1).FirstOrDefault()}]";
            }

            //need to s
            var reactorProcess = new ProcessStartInfo
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                ErrorDialog = false,
                WorkingDirectory = Environment.CurrentDirectory,
            };

            var tokens = app.AppPath.Split(' ');
            if (tokens.Length > 1)
            {
                reactorProcess.FileName = tokens[0];// @"C:\jan\Rxns\Rxns.AppHost\bin\Debug\netcoreapp3.1\Rxn.Create.exe",
                reactorProcess.Arguments = tokens.Skip(1).Concat(args).ToStringEach(" ");
            }
            else
            {
                reactorProcess.FileName = app.AppPath;
                reactorProcess.Arguments = args.ToStringEach(" ");
            }

            _process = new Process
            {
                StartInfo = reactorProcess,
                EnableRaisingEvents = true
            };

            _process.Exited += Process_Exited;
        }


        private void Process_Exited(object sender, EventArgs e)
        {
            LogExitReason();


            //st.Publish(new AppProcessEnded());
            _status.OnNext(ProcessStatus.Terminated);

            //// If Process Status is Active is because it was running and ended unexpectedly, 
            //// If status is Terminated then it was due to Terminate method invoked.
            //if (RestartOnProcessExit && _processStatus != ProcessStatus.Killed)
            //{
            //    $"Restarting process {args.Last()}".LogDebug();
            //    try
            //    {
            //        Start();
            //    }
            //    catch (Exception ex)
            //    {
            //        $"failed to restart {ex}".LogDebug();
            //    }
            //}
        }

        public bool RestartOnProcessExit => true;

        /// <summary>
        /// Logs the current action taken.
        /// </summary>
        private void LogExitReason()
        {
            switch (_processStatus)
            {
                case ProcessStatus.Active:
                    "process exited unexpectedly".LogDebug(_name);
                    break;
                case ProcessStatus.Killed:
                    "process was ended".LogDebug(_name);
                    break;
                case ProcessStatus.Terminated:
                    "process restarted".LogDebug(_name);
                    break;
            }
        }
        
        /// <summary>
        /// Starts the remote process which will host an Activator
        /// </summary>
        public IObservable<IRxnAppContext> Start()
        {
            $"Starting process '{_name}'".LogDebug();

            if (!_process.Start())
            {
                throw new Exception(string.Format("Failed to start process from: {0}", _process.StartInfo.FileName));
            }

            _process.KillOnExit();
            $"Process successfully started with process id {_process.Id}".LogDebug();

            //_centralManager.Publish(new AppProcessStarted());
            _status.OnNext(ProcessStatus.Active);

            return this.ToObservable();
        }

        /// <summary>
        /// Terminates this process, then it starts again.
        /// </summary>
        public void Terminate()
        {
            $"Terminating {_name}".LogDebug();

            _processStatus = ProcessStatus.Terminated;

            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill();
                }
            }
            catch (Exception ex)
            {
                $"Failed to terminate {ex}".LogDebug();

                throw;
            }
        }

        /// <summary>
        /// Kills the remote process.
        /// </summary>
        public void Kill()
        {
            $"Killing {_name}".LogDebug();

            _processStatus = ProcessStatus.Killed;

            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill();
                    _process.WaitForExit();
                }
            }
            catch (Exception ex)
            {
                $"Failed to kill {ex}".LogDebug();
                throw;
            }
        }


        public void Dispose()
        {
            _resources.Dispose();
            _resources.Clear();
        }

        public void OnDispose(IDisposable obj)
        {
            _resources.Add(obj);
        }
    }



}
