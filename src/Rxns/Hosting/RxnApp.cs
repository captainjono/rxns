using System;
using System.Collections.Generic;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Rxns.Health.AppStatus;
using Rxns.Interfaces;
using Rxns.Logging;
using Rxns.Metrics;
using Rxns.Microservices;

namespace Rxns.Hosting
{
    public class DynamicStartupTask : IContainerPostBuildService
    {
        private Action<IReportStatus, IResolveTypes> _toRun;

        public DynamicStartupTask(Action<IReportStatus, IResolveTypes> toRunOnStartup)
        {
            _toRun = toRunOnStartup;
        }

        public void Run(IReportStatus logger, IResolveTypes container)
        {
            _toRun(logger, container);
        }
    }

    public class RxnApp : ReportsStatus, IRxnApp, IAppEvents
    {
        private readonly IObservable<IDisposable> _inline;
        private readonly IRxnAppFactory _rxnFactory;

        public IRxnDef Definition { get; }
        public IAppSetup Installer => new NoInstaller();


        public RxnApp(Type app, IRxnDef def, IRxnAppFactory rxnFactory)
        {
            //_app = def.Container.Resolve(app) as IMicroApp;
            Definition = def;
            _rxnFactory = rxnFactory;
        }

        public RxnApp(IObservable<IDisposable> inline, IRxnDef def, IRxnAppFactory rxnFactory)
        {
            _inline = inline;
            Definition = def;
            _rxnFactory = rxnFactory;

            Definition.UpdateWith(l =>
            {
                l.CreatesOncePerApp(_ => new DynamicStartupTask((_, __) => inline.Until()));
            });
        }

        public RxnApp(IRxnDef def, IRxnAppFactory rxnFactory)
        {
            Definition = def;

            _rxnFactory = rxnFactory;
        }

        public IObservable<IRxnAppContext> Start(bool startRxns = true, IAppContainer container = null)
        {
            var app = _rxnFactory.Create(this, this, container ?? Definition.Container, RxnSchedulers.TaskPool);
            
            return app.Start(startRxns, container).Do(appContext=>
            {
                if (startRxns)
                {
                    var preStartupServices = appContext.Resolver.Resolve<IContainerPostBuildService[]>();
                    preStartupServices.ForEach(s =>
                    {
                        try
                        {
                            s.Run(this, app.Resolver);
                        }
                        catch (Exception e)
                        {
                            OnError(e);
                        }
                    });
                }

                _onStart.OnNext(new Unit());
            });
        }
        
        public static Func<string, Action<IRxnLifecycle>> SpareReator = appStatusUrl => spaceReactor =>
        {
            appStatusUrl ??= "http://localhost:888";

            spaceReactor
                .Includes<AppStatusClientModule>()
                .Includes<RxnsModule>()
                .CreatesOncePerApp<NoOpSystemResourceService>()
                .CreatesOncePerApp(_ => new ReliableAppThatHeartbeatsEvery(TimeSpan.FromSeconds(10)))
                .CreatesOncePerApp<INSECURE_SERVICE_DEBUG_ONLY_MODE>()
                .CreatesOncePerApp(() => new AggViewCfg()
                {
                    ReportDir = "reports"
                })
                .CreatesOncePerApp(() => new AppServiceRegistry()
                {
                    AppStatusUrl = appStatusUrl
                })
                .CreatesOncePerApp<UseDeserialiseCodec>();
        };


        private readonly ReplaySubject<Unit> _onStart = new ReplaySubject<Unit>();
        public IObservable<Unit> OnStart => _onStart.ObserveOn(CurrentThreadScheduler.Instance).SubscribeOn(CurrentThreadScheduler.Instance);
    }

    public class NoInstaller : IAppSetup
    {
        public void Dispose()
        {
            
        }

        public TimeSpan Timeout { get; set; }
        public void Install()
        {
            "App does not have an installer".LogDebug();
        }

        public void Uninstall()
        {
            "App does not have an uninstaller".LogDebug();

        }
    }
}
