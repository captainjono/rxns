using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Rxns.DDD;
using Rxns.DDD.Commanding;
using Rxns.DDD.CQRS;
using Rxns.Hosting.Cluster;
using Rxns.Hosting.Updates;
using Rxns.Interfaces;
using Rxns.Logging;
using Rxns.Microservices;

namespace Rxns.Hosting
{

    public class ExternalProcessRxnAppContext : IRxnAppContext
    {
        private readonly IStoreAppUpdates _appStore;
        public IRxnHostableApp App { get; }
        public string[] args { get; set; }
        readonly List<IDisposable> _resources = new List<IDisposable>();
        private OutOfProcessRxnAppContext _container;
        private string _lastAppVersion;
        private IObservable<IRxnAppContext> _appStarted;


        public IAppSetup Installer => App.Installer;
        public ICommandService CmdService => new RxnManagerCommandService(RxnManager,Resolver.Resolve<ICommandFactory>(), Resolver.Resolve<IServiceCommandFactory>());
        public IAppCommandService AppCmdService => Resolver.Resolve<IAppCommandService>();
        public IRxnManager<IRxn> RxnManager { get; }
        public IResolveTypes Resolver { get; }
        public IObservable<ProcessStatus> Status => _status;

        private ISubject<ProcessStatus> _status = new BehaviorSubject<ProcessStatus>(ProcessStatus.Terminated);

        public ExternalProcessRxnAppContext(IRxnHostableApp app, string[] args, IRxnManager<IRxn> rxnManager, IStoreAppUpdates appStore)
        {
            _appStore = appStore;
            App = app;
            this.args = args;

            RxnManager = rxnManager;

            if (App.Container == null)
            {
                App.Definition.Build();
            }

            Resolver = App.Container;

            
        }

        public IObservable<IRxnAppContext> Start(bool shouldStartRxns = true, IAppContainer container = null)
        {
            return Rxn.Create(() =>
            {
                if (_status.Value() == ProcessStatus.Active)
                {
                    "already started".LogDebug();
                }
                else
                {
                    var rxnCfg = RxnAppCfg.Detect(args);

                    Start(App, rxnCfg.Version);
                }

                return _appStarted;
            });
        }

        public void Terminate()
        {
            Stop();
        }

        /// <summary>
        /// Starts the hosted app up
        /// </summary>
        public IRxnAppProcessContext Start(IRxnHostableApp rxn, string version = null, bool forceInstall = false)
        {
            try
            {
                //if we have different version, dispose of the container
                if (version != _lastAppVersion)
                {
                    DisposeContainer();
                }

                var app = GetOrCreateApp(rxn, version);

                //if we have a different version, install it
                if (forceInstall || version != _lastAppVersion)
                {
                    Install(App);
                    TruncateBackups();
                }

                LogAsHost("Starting app");
                LaunchApp(app, version);
            }
            catch (Exception e)
            {
                LogAsHost("[Error] App failed to start: {0}", e);
                DisposeContainer();

                if (version != _lastAppVersion)
                {
                    LogAsHost("[Error] Starting the previous version of App instead");
                    Start(App, _lastAppVersion, true);
                }
            }

            return this;
        }

        /// <summary>
        /// Starts the app and sets the current version
        /// if successful
        /// </summary>
        /// <param name="app">the app to run</param>
        /// <param name="version">the apps version</param>
        private void LaunchApp(OutOfProcessRxnAppContext app, string version)
        {
            
            _container.Start().Do(_ =>
            {
                _.OnDispose(_container.Status.Subscribe(_status));
                SetCurrentVersion(version); //only if the app successfully starts, do we want the version to be set
            }).Until();
            //app.Start();

        }

        private OutOfProcessRxnAppContext GetOrCreateApp(IRxnHostableApp app, string version)
        {
            var targetPath = _appStore.Run(new GetAppDirectoryForAppUpdate(app.AppInfo.Name, version)).WaitR();//todo: remove waitr
            
            //index any dotnet commands by this targetPath
            if(app.AppPath.StartsWith("dotnet"))
            {
                app.AppInfo.Version = version;
                var appBinary = new FileInfo(app.AppPath.Split(' ').Skip(1).FirstOrDefault()).Name;
                app.AppPath = $"dotnet {Path.Combine(targetPath, appBinary)}";
            }

            if (_container == null)
            {
                _container = new OutOfProcessRxnAppContext(app, RxnManager, args, targetPath);
                _container.Status.Subscribe(_status).DisposedBy(_container);

                return _container;
            }

            return _container;
        }

        private void SetCurrentVersion(string version)
        {
            LogAsHost(String.Format("Setting version: '{0}' => '{1}'", _lastAppVersion, version));
            _lastAppVersion = version;
            //_config.AppVersion = version;
        }

        public void TruncateBackups()
        {
        //    //at the moment im making an assumption that all dirs 
        //    //under the root dir are managed by apphost
        //    var dirs = Directory.GetDirectories(App.AppPath).ToArray();
        //    var toKeep = dirs.OrderByDescending(dir => dir).Take(3 + 1/*the current app*/); //reverse alphabetical order gives us a proper version heirachy

        //    foreach (var oldBackup in dirs.Except(toKeep))
        //        Directory.Delete(oldBackup);
        }

        private void Install(IRxnHostableApp app)
        {
            LogAsHost($"Executing install of app {app.Installer.GetType().Name}");
            app.Installer.Install();
        }

        private void ResetApp(string version)
        {
            if (_container == null)
                return;

            LogAsHost("Resetting '{0}' at apps request", App.GetType());

            try
            {
                StopApp(_container);
                DisposeContainer();
            }
            catch (Exception e)
            {
                LogAsHost("[ERROR] while restarting: '{0}'", e);
            }
            finally
            {
                Start(App, version);
            }
        }

        /// <summary>
        /// Stops the hosted app, disposing off it before
        /// unloading its Isolated Process
        /// </summary>
        private void StopApp(OutOfProcessRxnAppContext app)
        {
            LogAsHost("Stopping app");

            try
            {
                app.Dispose();
            }
            catch (Exception e)
            {
                LogAsHost("[WARNING] while stopping app, disconnecting anyway: {0}", e.ToString());
            }

        }

        public void Stop()
        {
            if (_container != null)
            {
                StopApp(_container);
                _appStarted = null;
                _status.OnNext(ProcessStatus.Terminated);
            }
        }

        private void LogAsHost(string message, params object[] formatParams)
        {
            message.FormatWith(formatParams).LogDebug(this);
        }

        public void Dispose()
        {
            Stop();
            DisposeContainer();
            _resources.DisposeAll();
            _resources.Clear();
        }

        private void DisposeContainer()
        {
            if (_container != null)
            {
                LogAsHost("Disposing container");

                _container.Dispose();
                _container = null;
            }
        }

        public void OnDispose(IDisposable obj)
        {
            _resources.Add(obj);
        }
    }
}



//public Type[] ScanForImplementingAssemblies(string baseDir)
//{
//    var currentAssembly = Assembly.GetExecutingAssembly().Location;
//    var assemblies = new DirectoryInfo(@baseDir).EnumerateFileSystemInfos("*.dll").Where(a => a.FullName != currentAssembly).Select(
//        a =>
//        {
//            try
//            {
//                return Assembly.ReflectionOnlyLoadFrom(a.FullName);
//            }
//            catch (Exception e)
//            {
//                Console.WriteLine("{0}", e);
//            }

//            return null;
//        }).Where(x => x != null).ToArray();

//    var updatables = assemblies.SelectMany(a =>
//    {
//        try
//        {
//            return a.GetTypes().Where(t => t.GetInterface("IUpdateble") != null);
//        }
//        catch (Exception e)
//        {
//            Console.WriteLine(e);
//            return new List<Type>();
//        }
//    }).Where(a => a != null).ToArray();

//    return updatables;
//}
