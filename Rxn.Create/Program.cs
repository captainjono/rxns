using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading;
using Autofac;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Rxns;
using Rxns.AppHost;
using Rxns.DDD;
using Rxns.DDD.Commanding;
using Rxns.DDD.CQRS;
using Rxns.Health;
using Rxns.Health.AppStatus;
using Rxns.Hosting;
using Rxns.Hosting.Cluster;
using Rxns.Hosting.Updates;
using Rxns.Interfaces;
using Rxns.Logging;
using Rxns.Metrics;
using Rxns.Reliability;

namespace RxnCreate
{
    class Program
    {
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
        
        public static IObservable<Unit> SpawnFromAppUpdate(string appName, string version, string binary, string appLocation)
        {
            if (appLocation.StartsWith("."))
                appLocation = Environment.CurrentDirectory;

            "Spawning".LogDebug();
            $"App: {appName}".LogDebug();
            $"Version: {version}".LogDebug();
            $"Location: {appLocation}".LogDebug();
            
            var client = new AppUpdateServiceClient(new FileSystemAppUpdateRepo(new DotNetFileSystemService()), new DotNetFileSystemService());
            client.ReportToDebug();

            return client.Download(appName, version, appLocation, true).Do(_ =>
            {
                //spawn new app
                Process.Start(new ProcessStartInfo(Path.Combine(appLocation, binary)));
            });
        }

        public static IObservable<Unit> FromAppUpdate(string appName, string version, string binary, string appLocation, bool isLocal, string appStatusUrl = "http://localhost:888")
        {
            if (appLocation.StartsWith("."))
                appLocation = Environment.CurrentDirectory;

            "Spawning".LogDebug();
            $"App: {appName}".LogDebug();
            $"Version: {version}".LogDebug();
            $"Location: {appLocation}".LogDebug();

            var client = AutoSelectUpdateServerClient(isLocal, appStatusUrl);

            return client.Download(appName, version, appLocation, true).Do(_ =>
            {
                //spawn new app
                new RxnAppCfg()
                {
                    SystemName = appName,
                    Version = version,
                    PathToExe = Path.Combine(appLocation, binary)
                }.Save();

                var restart = Process.GetCurrentProcess().StartInfo;
                Process.Start(restart);
            });
        }

        public static IObservable<Unit> CreateAppUpdate(string appName, string version, string appLocation, bool isLocal, string appStatusUrl = "http://localhost:888")
        {
            if (appLocation.StartsWith("."))
                appLocation = Environment.CurrentDirectory;

            "Pushing update".LogDebug();
            $"App: {appName}".LogDebug();
            $"Version: {version}".LogDebug();
            $"Location: {appLocation}".LogDebug();

            var client = AutoSelectUpdateServerClient(isLocal, appStatusUrl);

            if (version.StartsWith("Latest-", StringComparison.OrdinalIgnoreCase))
            {
                version = version.Split('-')[1];
                version = $"{version}.{DateTime.Now.ToString("s").Replace(":", "")}";
            }
            
            return client.Upload(appName, version, appLocation);
        }

        private static IUpdateServiceClient AutoSelectUpdateServerClient(bool isLocal, string appStatusUrl)
        {
            var fs = new DotNetFileSystemService();
            
            if (!isLocal)
            {
                appStatusUrl ??= "http://localhost:888";
                $"Using AppStatus URL: {appStatusUrl}".LogDebug();

                var c = new AppUpdateServiceClient(new HttpUpdateServiceClient(new AppServiceRegistry()
                {
                    AppStatusUrl = appStatusUrl
                }, new AnonymousHttpConnection(new HttpClient()
                {
                    Timeout = TimeSpan.FromHours(24)
                }, new ReliabilityManager(new RetryMaxTimesReliabilityCfg(3)))), fs);
                c.ReportToDebug();
                return c;
            }
            else
            {
                "Saving update locally".LogDebug();

                var c = new AppUpdateServiceClient(new FileSystemAppUpdateRepo(fs), fs);
                c.ReportToDebug();
                return c;
            }
        }

        static void Main(string[] args)
        {

            GeneralLogging.Log.ReportToConsole();
            GeneralLogging.Log.ReportToDebug();
            
            //test with
            //PublishAppUpdate "sparereactor" "1.0" ".\"
            if (args.FirstOrDefault() == "NewAppUpdate")
            {
                var name = args.Skip(1).FirstOrDefault();
                var version = args.Skip(2).FirstOrDefault();
                var appLocation = args.Skip(3).FirstOrDefault();//?.Replace("\\\\", "\\");
                var isLocal = args.Skip(4).FirstOrDefault();
                var appStatusUrl = args.Skip(5).FirstOrDefault();

                CreateAppUpdate(name, version, appLocation, bool.Parse(isLocal.IsNullOrWhitespace(true.ToString())), appStatusUrl).Catch<Unit, Exception>(
                    e =>
                    {
                        GeneralLogging.Log.OnError("Push failed", e);
                        throw e;
                    }).WaitR();
                
                "Successfully created update".LogDebug();


                return;
            }

            //SpawnFromAppUpdate "sparereactor" "1.0" "Rxn.create.exe ".\" "islocal"
            if (args.FirstOrDefault() == "FromAppUpdate")
            {
                var systemName = args.Skip(1).FirstOrDefault();
                var version = args.Skip(2).FirstOrDefault();
                var binary = args.Skip(3).FirstOrDefault();
                var appLocation = args.Skip(4).FirstOrDefault();
                var isLocal = args.Skip(5).FirstOrDefault();
                var appStatusUrl = args.Skip(6).FirstOrDefault();
                
                FromAppUpdate(systemName, version, binary, appLocation, bool.Parse(isLocal.IsNullOrWhitespace(true.ToString())), appStatusUrl).WaitR();

                $"Successfully spawned {systemName} from update".LogDebug();

                return;
            }

            IRxnAppContext ctx = null;
            
            if (args.FirstOrDefault() == "SpareReactor")
            {
                "Running SpareReactor for auto scaleout".LogDebug();
                RunSpareReactor(url: args.Skip(1).FirstOrDefault(), args: args).Do(_ =>
                {
                    ctx = _;
                })
                .Until(GeneralLogging.Log.OnError);
            }
            else
            {

                var cfg = RxnAppCfg.Detect(args);
                //cfg.PathToExe = @"C:\Windows\System32\notepad.exe";
                //RxnAppCfg.Save(cfg);
                
                if (args.FirstOrDefault() == "ReactorFor" || !cfg.PathToExe.IsNullOrWhitespace())
                {
                    var pathToExe = args.Skip(1).FirstOrDefault();
                    RunSupervisorReactor(pathToExe ?? cfg.PathToExe, cfg.Version,  args: args).Do(_ => { ctx = _; })
                        .WaitR();

                    return;
                }

                if (!Debugger.IsAttached)// ; && args.Contains("Cache"))
                    Debugger.Launch();

                if (args.FirstOrDefault() == "Demo" || true)
                {
                    RunDemoAppReactor("http://192.168.1.77:888", args).Do(_ => { ctx = _; })
                        .Until(GeneralLogging.Log.OnError);
                }
                else
                {
                    RunSpareReactor("http://192.168.1.77:888", args).Do(_ => { ctx = _; })
                        .Until(GeneralLogging.Log.OnError);
                }
            }


            while (ctx == null)
            {
                Thread.SpinWait(10000);
                Thread.Yield();
            }

            ConsoleHostedApp.StartREPL(new RxnManagerCommandService(ctx.RxnManager, ctx.Resolver.Resolve<ICommandFactory>(), ctx.Resolver.Resolve<IServiceCommandFactory>()));
        }

        

        public static IObservable<IDisposable> SupervisedProcess(string pathToProcess)
        {
            return Rxns.Rxn.Create<IDisposable>(o =>
            {
                $"Starting {pathToProcess}".LogDebug();

                var reactorProcess = new ProcessStartInfo
                {
                    
                    ErrorDialog = false,
                    WorkingDirectory = new FileInfo(pathToProcess).DirectoryName,
                    FileName = pathToProcess,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };
                
                var p = new Process
                {
                    StartInfo = reactorProcess,
                    EnableRaisingEvents = true,
                };

                var nameOfProcess = new FileInfo(pathToProcess).Name;

                p.Start();
                
                p.StandardOutput.ReadToEndAsync().ToObservable().Do(msg =>
                {
                    msg.Result.LogDebug(nameOfProcess);
                });

                p.StandardError.ReadToEndAsync().ToObservable().Do(msg =>
                {
                    GeneralLogging.Log.OnError(new Exception(msg.Result), nameOfProcess);
                });

                p.Exited += (sender, args) =>
                {
                    GeneralLogging.Log.OnError($"{pathToProcess} exited");
                    o.OnCompleted();
                };


                p.KillOnExit();

                var exit = new DisposableAction(() =>
                {
                    $"Stopping supervisor for {pathToProcess}".LogDebug();
                    o.OnCompleted();
                });

                o.OnNext(exit);

                return exit;
            });
        }


        private static IObservable<IRxnAppContext> RunSupervisorReactor(string pathToExe, string version, string url = "http://localhost:888/", params string[] args)
        {
            return Rxns.Rxn.Create<IRxnAppContext>(o =>
            {
                $"Running supervisor for {pathToExe}".LogDebug();

                if (args.Contains("reactor"))
                {
                    OutOfProcessFactory.CreateNamedPipeClient(args.SkipWhile(a => a != "reactor").Skip(1).FirstOrDefault() ?? "spare");
                }
                else
                {
                    OutOfProcessFactory.CreateNamedPipeServer();
                }

                //setup static object.Serialise() & string.Deserialise() methods
                RxnExtensions.DeserialiseImpl = (t, json) => JsonExtensions.FromJson(json, t);
                RxnExtensions.SerialiseImpl = (json) => JsonExtensions.ToJson(json);

                var cfg = RxnAppCfg.Detect(args);
                
                return SpareReator(url)
                    .ToRxns(SupervisedProcess(pathToExe), args)
                    .Named(new ClusteredAppInfo(pathToExe.Split(new [] {'/', '\\'}).LastOrDefault(), version, args, false))
                    .OnHost(new ConsoleHostedApp(), cfg)
                    .Do(app =>
                    {
                        $"Advertising to {url}".LogDebug();
                        app.RxnManager.Publish(new PerformAPing()).Until(GeneralLogging.Log.OnError);
                    })
                    .Subscribe(o);
            });
        }
        
        private static IObservable<IRxnAppContext> RunDemoAppReactor(string url = "http://localhost:888/", params string[] args)
        {
            return Rxns.Rxn.Create<IRxnAppContext>(o =>
            {
                if (args.Contains("reactor"))
                {
                    OutOfProcessFactory.CreateNamedPipeClient(args.SkipWhile(a => a != "reactor").Skip(1).FirstOrDefault() ?? "spare");
                }
                else
                {
                    OutOfProcessFactory.CreateNamedPipeServer();
                }

                //setup static object.Serialise() & string.Deserialise() methods
                RxnExtensions.DeserialiseImpl = (t, json) => JsonExtensions.FromJson(json, t);
                RxnExtensions.SerialiseImpl = (json) => JsonExtensions.ToJson(json);

                var cfg = RxnAppCfg.Detect(args);

                return OutOfProcessDemo.DemoApp(SpareReator(url))
                    .ToRxns()
                    .Named(new ClusteredAppInfo("DemoApp", "1.0.0", args, false))
                    .OnHost(new ClusteredAppHost(
                            new OutOfProcessFactory(),
                            OutOfProcessFactory.RxnManager,
                            OutOfProcessFactory.PipeServer?.Router,
                            new AutoBalancingHostManager()
                                .ConfigureWith(new ConsoleHostedApp(), _ => true), cfg)
                                .ConfigureWith(new AutoScalingAppManager()
                                    .ConfigureWith(new ReliableAppThatRestartsOnCrash(OutOfProcessFactory.RxnManager))
                                    .ConfigureWith(new AutoScaleoutReactorPlan(new ScaleoutToEverySpareReactor(), "DemoApp", "Cache", "1.0")))
                                ,
                            cfg
                        )
                    .Do(app =>
                    {
                        $"Advertising to {url}".LogDebug();
                        app.RxnManager.Publish(new PerformAPing()).Until(GeneralLogging.Log.OnError);
                    })
                    .Subscribe(o);
            });
        }


        private static IObservable<IRxnAppContext> RunSpareReactor(string url = "http://localhost:888/", params string[] args)
        {
            return Rxns.Rxn.Create<IRxnAppContext>(o =>
            {
                //setup static object.Serialise() & string.Deserialise() methods
                RxnExtensions.DeserialiseImpl = (t, json) => JsonExtensions.FromJson(json, t);
                RxnExtensions.SerialiseImpl = (json) => JsonExtensions.ToJson(json);

                //think i need to read the rxn.cfg here to get the name of the app and populate it here. a sparereactor will then become
                //a supervisor for the app...
                //i will call that mode Supervisor?

                return SpareReator(url)
                    .ToRxns()
                    .Named(new AppVersionInfo("SpareReactor", "1.0.0", true))
                    .OnHost(new ConsoleHostedApp(), new RxnAppCfg())
                    .Do(app =>
                    {
                        $"Advertising to {url}".LogDebug();
                        app.RxnManager.Publish(new PerformAPing()).Until(GeneralLogging.Log.OnError);
                    })
                    .Subscribe(o);
            });
        }
    }


    public static class JsonExtensions
    {
        public static JsonSerializerSettings DefaultSettings = new JsonSerializerSettings()
        {
            ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor,
            TypeNameHandling = TypeNameHandling.Auto, ContractResolver = new Rxns.NewtonsoftJson.JsonExtensions.NonPubicPropertiesResolver()
        };

        private static readonly JsonSerializerSettings defaultsTo = new JsonSerializerSettings()
        {
            Formatting = Formatting.Indented, TypeNameHandling = TypeNameHandling.Auto,
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            SerializationBinder = new Rxns.NewtonsoftJson.JsonExtensions.DynamicTypeRenamingSerializationBinder()
        };

        public static string ToJson(this object str, ITraceWriter serialisationLog = null)
        {
            defaultsTo.TraceWriter = serialisationLog;
            return JsonConvert.SerializeObject(str, defaultsTo);
        }

        private static readonly JsonSerializerSettings defaultsFrom = new JsonSerializerSettings()
        {
            ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor,
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore, TypeNameHandling = TypeNameHandling.Auto,
            ContractResolver = new Rxns.NewtonsoftJson.JsonExtensions.NonPubicPropertiesResolver(),
            SerializationBinder = new Rxns.NewtonsoftJson.JsonExtensions.DynamicTypeRenamingSerializationBinder()
        };

        public static T FromJson<T>(this string json, ITraceWriter deserialisationLog = null)
        {
            defaultsFrom.TraceWriter = deserialisationLog;
            var obj = JsonConvert.DeserializeObject<T>(json, defaultsFrom);
            return obj;
        }

        public static object FromJson(this string json, Type targetType = null, ITraceWriter deserialisationLog = null,
            JsonSerializerSettings settings = null)
        {
            var s = settings ?? DefaultSettings;
            s.TraceWriter = deserialisationLog;
            return targetType == null
                ? JsonConvert.DeserializeObject(json, s)
                : JsonConvert.DeserializeObject(json, targetType, s);
        }


    }
}
