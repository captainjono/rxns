using System;
using System.IO;
using System.Reactive;
using System.Reactive.Linq;
using System.Reflection;
using Rxns.DDD.Commanding;
using Rxns.Hosting;
using Rxns.Hosting.Updates;
using Rxns.Interfaces;
using Rxns.Logging;
using Rxns.Microservices;

namespace Rxns
{
    public class RxnHostableApp : IRxnHostableApp
    {
        private readonly IRxnApp _app;
        private IRxnAppContext _context;
        public IRxnAppInfo AppInfo { get; }
        public IAppContainer Container => _app.Definition.Container;
        public IResolveTypes Resolver => _app.Definition.Container;

        public string AppPath { get; set; }
        public string AppBinary { get; } = "Rxn.Create.exe";
        public IObservable<Unit> MigrateTo(string gsystemName, string version)
        {
            "Migrate to new verison not implemented".LogDebug();
            
            if (gsystemName == AppInfo.Name && version == AppInfo.Version)
            {
                "Bypassing since app is already at this version".LogDebug();
                return new Unit().ToObservable();
            }

            AppPath = Path.Combine(Environment.CurrentDirectory, version, AppBinary);
            new RxnAppCfg(){ Version = version }.Save();

            _context.Resolver.Resolve<IRxnHost>().Restart(version);

            return new Unit().ToObservable();
        }
        
        public string GetDirectoryForVersion(string version)
        {
            return Path.Combine(Environment.CurrentDirectory, version);
        }

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

            app.Definition.UpdateWith(lifecycle => { lifecycle.CreatesOncePerApp(() => appInfo); });
        }

        public IObservable<IRxnAppContext> Start()
        {
            //do we run the installer here?
            return _app.Start().Do(c =>
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
