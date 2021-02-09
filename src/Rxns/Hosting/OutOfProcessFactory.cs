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
using Rxns.Hosting.Updates;
using Rxns.Interfaces;
using Rxns.Logging;
using Rxns.Playback;

namespace Rxns.Hosting
{
    public class OutOfProcessFactory : IRxnAppProcessFactory
    {
        private readonly IStoreAppUpdates _appStore;
        public static RxnManager<IRxn> RxnManager { get; set; }

        public OutOfProcessFactory(IStoreAppUpdates appStore)
        {
            _appStore = appStore;
        }
        
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
                        return new ExternalProcessRxnAppContext(app, args, RxnManager, _appStore).ToObservable().Subscribe(o);
                    case RxnMode.InProcess:
                        return hostManager.GetHostForApp(reactorName).Stage(app, new RxnAppCfg() { Args = args }).SelectMany(h => h.Run()).Subscribe(o);
                    default:
                        "cant dertermine reactor mode, defaulting to inprocess".LogDebug();
                        return hostManager.GetHostForApp(reactorName).Stage(app, new RxnAppCfg() { Args = args }).SelectMany(h => h.Run()).Subscribe(o);
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
                        return new ExternalProcessRxnAppContext(app, "reactor main".Split(), RxnManager, _appStore).ToObservable().Subscribe(o);
                    //should also then startup the supervisor host!
                    case RxnMode.OutOfProcess:
                        var reactorName = args.Reverse().FirstOrDefault();
                        var routes = app.Resolver.Resolve<IManageReactors>().GetOrCreate(reactorName).Reactor.Molecules.SelectMany(m => RxnCreator.DiscoverRoutes(m)).ToArray();
                        PipeServer.ListenForNewClient(reactorName, routes);

                        return new ExternalProcessRxnAppContext(app, args, RxnManager, _appStore).ToObservable().Subscribe(o);
                    case RxnMode.InProcess:
                        return hostManager.GetHostForApp(null).Stage(app, new RxnAppCfg() { Args = args }).SelectMany(h => h.Run()).Subscribe(o);
                    case RxnMode.Main:
                        return hostManager.GetHostForApp("main").Stage(app, new RxnAppCfg() { Args = args }).SelectMany(h => h.Run()).Subscribe(o);
                    default:
                        "cant dertermine mode, defaulting to inprocess".LogDebug();
                        return hostManager.GetHostForApp(null).Stage(app, new RxnAppCfg() { Args = args }).SelectMany(h => h.Run()).Subscribe(o);
                }
            });
        }

    }
}
