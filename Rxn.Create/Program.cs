using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
using Rxns.Hosting;
using Rxns.Hosting.Cluster;
using Rxns.Interfaces;
using Rxns.Logging;

namespace RxnCreate
{
    class Program
    {

        static void Main(string[] args)
        {
            ReportStatus.Log.ReportToDebug();
            
            //args = "NewAppUpdate DotNetTestWorker Latest-1 /Applications/JanisonReplay.app false false http://192.168.1.2:888/".Split(' ');

            //args = "SpareReactor http://192.168.1.2:888".Split(' ');
            //test with
            //PublishAppUpdate "sparereactor" "1.0" ".\"
            if (args.FirstOrDefault() == "NewAppUpdate")
            {
                var name = args.Skip(1).FirstOrDefault();
                var version = args.Skip(2).FirstOrDefault();
                var appLocation = args.Skip(3).FirstOrDefault().IsNullOrWhiteSpace(".");//?.Replace("\\\\", "\\");
                var isLocal = args.Skip(4).FirstOrDefault();
                var generateRxnCfg = bool.Parse(args.Skip(5).FirstOrDefault() ?? false.ToString());
                var appStatusUrl = args.Skip(6).FirstOrDefault();

                if (appLocation == ".")
                    appLocation = Directory.GetCurrentDirectory();

                appLocation.LogDebug("APPLOC");
                if(generateRxnCfg)
                    new RxnAppCfg()
                    {
                        SystemName = name,
                        Version = version,
                        AppStatusUrl = appStatusUrl,
                        KeepUpdated = true,
                    }.Save(appLocation);

                RxnApps.CreateAppUpdate(name, version, appLocation, bool.Parse(isLocal.IsNullOrWhiteSpace(true.ToString())), appStatusUrl).Catch<Unit, Exception>(
                    e =>
                    {
                        ReportStatus.Log.OnError("Push failed", e);
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
                
                RxnApps.FromAppUpdate(systemName, version, binary, appLocation, bool.Parse(isLocal.IsNullOrWhiteSpace(true.ToString())), appStatusUrl).WaitR();

                $"Successfully spawned {systemName} from update".LogDebug();

                return;
            }

            IRxnAppContext ctx = null;
            
            if (args.FirstOrDefault() == "SpareReactor")
            {
                "Running SpareReactor for auto scaleout".LogDebug();
                RunSpareReactor(url: args.Skip(1).FirstOrDefault()).Do(_ =>
                {
                    ctx = _;
                })
                .Until();
            }
            else
            {
                var cfg = RxnAppCfg.Detect(args);
                //cfg.AppPath = @"C:\Windows\System32\notepad.exe";
                //RxnAppCfg.Save(cfg);
                
                if (args.FirstOrDefault() == "ReactorFor" || !cfg.AppPath.IsNullOrWhitespace())
                {
                    var pathToApp = args.Skip(1).FirstOrDefault();
                    var url =  args.Skip(2).FirstOrDefault();

                    RxnProcessSupervisor.RunSupervisorReactor(pathToApp ?? cfg.AppPath, cfg.Version, url,  args).Do(_ => { ctx = _; }).WaitR();

                    return;
                }

                if (!Debugger.IsAttached)// ; && args.Contains("Cache"))
                    Debugger.Launch();

                if (args.FirstOrDefault() == "Demo" || true)
                {
                    RunDemoAppReactor(args: args).Do(_ => { ctx = _; })
                        .Until();
                }
                else if (args.FirstOrDefault() == "TestAgent"|| true)
                {
                    UnitTestAgent.RunTestAgentReactor(args: args).Do(_ => { ctx = _; })
                        .Until();
                }
                else
                {
                    RunSpareReactor().Do(_ => { ctx = _; })
                        .Until();
                }
            }


            while (ctx == null)
            {
                Thread.SpinWait(10000);
                Thread.Yield();
            }

            ConsoleHostedApp.StartREPL(new RxnManagerCommandService(ctx.RxnManager, ctx.Resolver.Resolve<ICommandFactory>(), ctx.Resolver.Resolve<IServiceCommandFactory>()));
        }
        
        public static IObservable<IRxnAppContext> RunSpareReactor(string url = "http://localhost:888/", Func<Action<IRxnLifecycle>, Action<IRxnLifecycle>> mod = null)
        {
            return Rxns.Rxn.Create<IRxnAppContext>(o =>
            {
                //setup static object.Serialise() & string.Deserialise() methods
                RxnExtensions.DeserialiseImpl = (t, json) => JsonExtensions.FromJson(json, t);
                RxnExtensions.SerialiseImpl = (json) => JsonExtensions.ToJson(json);

                mod = mod ?? (_ => _);
                //think i need to read the rxn.cfg here to get the name of the app and populate it here. a sparereactor will then become
                //a supervisor for the app...
                //i will call that mode Supervisor?

                return mod(RxnApp.SpareReator(url))
                    .ToRxns()
                    .Named(new AppVersionInfo("SpareReactor", "1.0.0", true))
                    .OnHost(new ConsoleHostedApp(), new RxnAppCfg())
                    .Do(app =>
                    {
                        $"Advertising to {url}".LogDebug();
                        app.RxnManager.Publish(new PerformAPing()).Until(ReportStatus.Log.OnError);

                        $"Streaming logs".LogDebug();
                        app.RxnManager.Publish(new StreamLogs()).WaitR();
                    })
                    .Subscribe(o);
            });
        }
        
        public class TestCfg : StartUnitTest
        {
            public static TestCfg Detect()
            {
                var cfg = new TestCfg();
                if (File.Exists("unittest.cfg"))
                {
                    cfg = File.ReadAllText("unittest.cfg").FromJson<TestCfg>();
                }

                return cfg;
            }
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

                return OutOfProcessDemo.DemoApp(RxnApp.SpareReator(url))
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
                        app.RxnManager.Publish(new PerformAPing()).Until(ReportStatus.Log.OnError);
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
