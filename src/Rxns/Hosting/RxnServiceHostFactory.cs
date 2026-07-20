//using System;
//using System.Collections.Concurrent;
//using System.Collections.Generic;
//using System.IO;
//using System.Reactive;
//using System.Reactive.Linq;
//using Rxns.AutoUpdate;
//using Rxns.AutoUpdate.Interface;
//using Rxns.DDD.Commanding;
//using Rxns.Logging;
//using Rxns.Metrics;

//namespace Rxns.Hosting
//{
//    public interface IRxnAppHost
//    {

//    }

//    public interface IRxnMicroserviceHostFactory
//    {
//        IRxnAppHost CreateWith(IRxnAppCfg cfg);
//    }

//    //public class ExternalProcessMicroserviceHostFactory : IRxnMicroserviceHostFactory
//    //{
//    //    public IRxnAppHost CreateWith(IRxnAppCfg cfg)
//    //    {
//    //        return new AppHost(cfg, () => new ProcessDomainContainer(), new WindowsFileSystem());
//    //    }
//    //}

//    /// <summary>
//    /// Starts up a host and adds it as a member of the managers pool
//    /// </summary>
//    public class SpinUpHost : ServiceCommand
//    {
//        public string Alias { get; private set; }
//        public string AppTypeName { get; private set; }
//        public string AppDll { get; private set; }
//        public string Version { get; private set; }
//        public bool ShouldOverwrite { get; private set; }

//        public SpinUpHost(string alias, string appTypeName, string appDll, string version, string shouldOverwriteBool)
//        {
//            Alias = alias;
//            AppTypeName = appTypeName;
//            Version = version;
//            ShouldOverwrite = shouldOverwriteBool.AsBool();
//            AppDll = appDll;
//        }
//    }

//    /// <summary>
//    /// Stops a host but does not remove it from the pool
//    /// </summary>
//    public class SpinDownHost : ServiceCommand
//    {
//        public string Alias { get; private set; }

//        public SpinDownHost(string @alias)
//        {
//            Alias = alias;
//        }
//    }

//    /// <summary>
//    /// Removes a host permantly from the managers pool
//    /// </summary>
//    public class KillHost : ServiceCommand
//    {
//        public string Alias { get; private set; }

//        public KillHost(string @alias)
//        {
//            Alias = alias;
//        }
//    }

//    public class ListHostedApps : ServiceCommand
//    {

//    }

//    /// <summary>
//    /// A microservice manager can spin up one of more microservices that will become
//    /// members of its pool which is persitant across restarts
//    /// 
//    /// 
//    /// todo: microservice cfg should be saved and then run again on startup to cator for
//    /// service restarts
//    /// </summary>
//    public class RxnAppManager : ReportStatusService,
//                                                IServiceCommandHandler<SpinUpHost>,
//                                                IServiceCommandHandler<SpinDownHost>,
//                                                IServiceCommandHandler<KillHost>,
//                                                IServiceCommandHandler<ListHostedApps>
//    {
//        private readonly IRxnMicroserviceHostFactory _appHostFactory;
//        private readonly IFileSystemConfiguration _fsCfg;
//        /// <summary>
//        /// this is public because of a circular depdency i couldnt inject via the constructor.
//        /// SET ME BEFORE USING
//        /// </summary>
//        public IUpdateServiceClient _appRepository { get; set; }

//        private class AppMeta
//        {
//            public IAppConfig Cfg { get; set; }
//            public AppHost App { get; set; }

//        }
//        private readonly IDictionary<string, AppMeta> _apps = new ConcurrentDictionary<string, AppMeta>();

//        public RxnAppManager(IServiceCommandFactory cmdFactory, IRxnMicroserviceHostFactory appHostFactory, IFileSystemConfiguration fsCfg)
//        {
//            _appHostFactory = appHostFactory;
//            _fsCfg = fsCfg;
//            //_appRepository = appRepository; //circular depdency
//        }

//        public IObservable<CommandResult> Handle(SpinUpHost cmd)
//        {
//            return Rxn.Create(() =>
//            {
//                OnInformation("[{0}] Staging ...", cmd.Alias);

//                if (_apps.ContainsKey(cmd.Alias))
//                    return CommandResult.Failure("[{0}] Ow :( Im already online. Spin me down first please.".FormatWith(cmd.Alias)).ToObservable();

//                return CreateHostForAppWith(cmd).Select(_ =>
//                {
//                    OnInformation("[{0}] Starting ...", cmd.Alias);

//                    _apps[cmd.Alias].App.Start();

//                    return CommandResult.Success();
//                });
//            });
//        }

//        private IObservable<IAppConfig> CreateHostForAppWith(SpinUpHost cmd)
//        {
//            return Rxn.Create(() =>
//            {
//                OnInformation("[{0}] Create microservice host with '{1}' from '{2}' @ v{3}", cmd.Alias, cmd.AppTypeName, cmd.AppDll, cmd.Version);

//                //SpinUp AggSyncServer Rxn.AggSyncServer.AggSyncServerBootstrap Rxn.AggSyncServer 1.0
//                var appDir = Path.Combine(_fsCfg.TemporaryDirectory, cmd.Alias);
//                var cfg = new InMemoryConfig()
//                {
//                    AppBackupHistory = 1,
//                    AppPlatform = "x64",
//                    AppCompilerVersion = "v4.0",
//                    AppDll = "{0}, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null".FormatWith(cmd.AppDll.Replace(".exe", "").Replace(".dll", "")),
//                    SystemName = "RvMS_{0}".FormatWith(cmd.Alias),
//                    AppType = cmd.AppTypeName,
//                    //AppVersion = cmd.Version,
//                    AppRootPath = appDir,
//                    AppSettingsFilename = "{0}.config".FormatWith(cmd.AppDll)
//                };

//                return _appRepository.Download(cmd.Alias, cmd.Version, appDir, cmd.ShouldOverwrite)
//                                    .Select(_ =>
//                                    {
//                                        var app = _appHostFactory.CreateWith(cfg);

//                                        //since the app ref will be lost on disconnection, these wont leak?
//                                        app.LogApp += msg => OnInformation("[{0}] {1}", cmd.Alias, msg);
//                                        app.LogHost += msg => OnInformation("[{0}] {1}", cmd.Alias, msg);

//                                        _apps.Add(cmd.Alias, new AppMeta() { App = app, Cfg = cfg });

//                                        return cfg;
//                                    });
//            });
//        }

//        public IObservable<CommandResult> Handle(KillHost cmd)
//        {
//            return Rxn.Create(() =>
//            {
//                if (!_apps.ContainsKey(cmd.Alias))
//                    return CommandResult.Failure("No app with alias '{0}' is hosted here sorry!");

//                OnInformation("Killing '{0}'", cmd.Alias);
//                _apps[cmd.Alias].App.Dispose();
//                _apps.Remove(cmd.Alias);

//                return CommandResult.Success();
//            });
//        }

//        public IObservable<CommandResult> Handle(ListHostedApps command)
//        {
//            return Rxn.Create(() =>
//            {
//                foreach (var appsKey in _apps.Keys)
//                {
//                    OnInformation("Hosting '{0}' using '{1}'", appsKey, _apps[appsKey].Cfg.AppDll);
//                }

//                return CommandResult.Success();
//            });
//        }

//        public IObservable<CommandResult> Handle(SpinDownHost cmd)
//        {
//            return Rxn.Create(() =>
//            {
//                if (!_apps.ContainsKey(cmd.Alias))
//                    return CommandResult.Failure("No app with alias '{0}' is hosted here sorry!");

//                OnInformation("Stopping '{0}'", cmd.Alias);
//                _apps[cmd.Alias].App.Stop();

//                return CommandResult.Success();
//            });
//        }
//    }
//}
