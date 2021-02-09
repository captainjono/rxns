using System;
using System.Reactive.Linq;
using System.Reflection;
using Rxns.Hosting;
using Rxns.Interfaces;
using Rxns.Microservices;

namespace Rxns
{
    public class RxnHostableApp : IRxnHostableApp
    {
        private readonly IRxnApp _app;
        private IRxnAppContext _context;
        public IRxnAppInfo AppInfo { get; }
        public IAppContainer Container { get; set; }
        public IResolveTypes Resolver => (Container ?? _context?.Resolver ?? _app.Definition.Container); //todo: seriously, wtf?? is that nullable statement

        public string AppPath { get; set; }
        public void Use(IAppContainer container)
        {
            Container = container;
            //_app.Use(container)
        }

        public string AppBinary { get; } = "Rxn.Create.exe";

        public IRxnDef Definition => _app.Definition;
        public IAppSetup Installer => _app.Installer;

        public RxnHostableApp(IRxnApp app, IRxnAppInfo appInfo)
        {
            _app = app;
            AppInfo = appInfo;
            app.Definition.UpdateWith(d => d.CreatesOncePerApp(_ => appInfo));

            AppPath = Assembly.GetEntryAssembly().Location;

            if(AppPath.EndsWith(".dll"))
                AppPath = $"dotnet {AppPath}";
        }

        public IObservable<IRxnAppContext> Start(bool startRxns = true, IAppContainer container = null)
        {
            //do we run the installer here?
            return _app.Start(startRxns, container).Do(c =>
            {
                _context = c;
            });
        }

        public void Dispose()
        {
            _app.Dispose();
        }
    }
}
