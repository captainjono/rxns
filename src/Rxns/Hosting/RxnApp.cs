using System;
using System.Reactive.Linq;
using Rxns.Logging;

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
