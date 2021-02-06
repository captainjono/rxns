using System;
using System.Reactive.Linq;
using Rxns.DDD.CQRS;
using Rxns.Health;
using Rxns.Health.AppStatus;
using Rxns.Logging;
using Rxns.Metrics;
using Rxns.NewtonsoftJson;

namespace Rxns.Hosting
{
    public class RxnApp : ReportsStatus, IRxnApp
    {
        private IMicroApp _app;
        private readonly IRxnAppFactory _rxnFactory;

        public IRxnDef Definition { get; }
        public IAppSetup Installer => new NoInstaller();
        public IRxnAppContext Context { get; }
        

        public RxnApp(Type app, IRxnDef def, IRxnAppFactory rxnFactory)
        {
            _app = def.Container.Resolve(app) as IMicroApp;
            Definition = def;
            _rxnFactory = rxnFactory;
        }

        public RxnApp(IMicroApp app, IRxnDef def, IRxnAppFactory rxnFactory)
        {
            _app = app;
            Definition = def;

            _rxnFactory = rxnFactory;
        }

        public IObservable<IRxnAppContext> Start()
        {
            var app = _rxnFactory.Create(_app, this, Definition, RxnSchedulers.TaskPool);

            return _app.Start().Select(_app => app);//.DisposedBy(app);
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
    }

    public class NoInstaller : IAppSetup
    {
        public void Dispose()
        {
            
        }

        public TimeSpan Timeout { get; set; }
        public void Install()
        {
            
        }

        public void Uninstall()
        {
            
        }
    }
}
