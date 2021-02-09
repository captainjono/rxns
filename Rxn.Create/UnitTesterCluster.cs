using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Autofac;
using Rxns;
using Rxns.Cloud;
using Rxns.DDD.Commanding;
using Rxns.DDD.CQRS;
using Rxns.Health;
using Rxns.Hosting;
using Rxns.Hosting.Updates;
using Rxns.Interfaces;
using Rxns.Logging;

namespace RxnCreate
{
    public class StartUnitTest : ServiceCommand
    {
        public bool RunAllTest { get; set; }
        public string RunThisTest { get; set; }
        public int RepeatTests { get; set; }
        public bool InParallel { get; set; }
        public string Dll { get; set; }
        public string UseAppUpdate { get; set; }
        public string UseAppVersion { get; set; }
        public string AppStatusUrl { get; set; }
    }

    public class UnitTestResult : CommandResult
    {
        public bool WasSuccessful { get; set; }
    }

    public class QueueWorkDone : CommandResult
    {
        
    }
    public class RxnManagerWorkerTunnel<T, TR> : IClusterWorker<T, TR>, IRxnCfg, IRxnPublisher<IRxn> where T: IUniqueRxn where TR : IRxnResult
    {
        IObservable<IRxn> _rxnManager;
        private Func<IRxn, IObservable<Unit>> _publish;

        public string Name { get; set; } = "RxnManagerWorker";
        public string Route { get; set; }
        
        
        public IObservable<TR> DoWork(T work)
        {
            return _rxnManager.Ask<TR>(work, _publish);
        }

        public string Reactor { get; set; } = "Workers";
        public IObservable<IRxn> ConfigureInput(IObservable<IRxn> pipeline)
        {
            return _rxnManager = pipeline;
        }

        public IDeliveryScheme<IRxn> InputDeliveryScheme { get; set; }
        public bool MonitorHealth { get; set; }
        public RxnMode Mode { get; set; }
        public void ConfigiurePublishFunc(Action<IRxn> publish)
        {
            _publish =e =>
            {
                publish(e);
                return new Unit().ToObservable();
            };
        }
    }
    
    /// <summary>
    /// 
    /// </summary>
    public class TestWorker : IClusterWorker<StartUnitTest, UnitTestResult>
    {
        private readonly IUpdateServiceClient _updateService;
        public string Name { get; }
        public string Route { get; }

        public TestWorker(string name, string route, IUpdateServiceClient updateService)
        {
            _updateService = updateService;
            Name = name;
            Route = route;
        }

        public IObservable<UnitTestResult> DoWork(StartUnitTest work)
        {
            $"Preparing to run {(work.RunAllTest ? "All" : work.RunThisTest)}".LogDebug();

            if (!Directory.Exists("logs"))
            {
                Directory.CreateDirectory("logs");
            }
            
            var file = File.Create($"logs/testLog_{DateTime.Now.ToString("s").Replace(":", "").LogDebug("LOG")}");
            var testLog = new StreamWriter(file, leaveOpen: true);
            var keepTestUpdatedIfRequested = work.UseAppUpdate.ToObservable(); //if not using updates, the dest folder is out root
            
            if (!work.UseAppUpdate.IsNullOrWhitespace())
            {
                //todo: update need
                keepTestUpdatedIfRequested = _updateService.KeepUpdated(work.UseAppUpdate, work.UseAppVersion, work.UseAppUpdate, new RxnAppCfg()
                {
                    AppStatusUrl = work.AppStatusUrl,
                    SystemName = work.UseAppUpdate,
                    KeepUpdated = true
                }, true);
            }

            return keepTestUpdatedIfRequested
                .Select(testPath =>
                {
                    $"Running {(work.RunAllTest ? "All" : work.RunThisTest)}".LogDebug();

                    //todo: make testrunner injectable/swapable
                    return Rxn.Create("dotnet", $"test{FilterIfSingleTestOnly(work)} {Path.Combine(testPath, work.Dll)}",
                        i => testLog.WriteLine(i.LogDebug(work.RunThisTest ?? work.Dll)),
                        e => testLog.WriteLine(e.LogDebug(work.RunThisTest ?? work.Dll))
                    );
                })
                .Switch()
                .LastOrDefaultAsync()
                .Select(_ =>
                {

                    return (UnitTestResult) new UnitTestResult()
                    {
                        WasSuccessful = true
                    }.AsResultOf(work);
                });
        }

        private string FilterIfSingleTestOnly(StartUnitTest work)
        {
            return work.RunThisTest.IsNullOrWhitespace() ? "" : $" --filter Name={work.RunThisTest}";
        }
    }

    public class UnitTesterCluster : ElasticQueue<StartUnitTest, UnitTestResult>, IRxnProcessor<StartUnitTest>, IManageResources
    {
        public ClusterFanOut<StartUnitTest, UnitTestResult> Workflow = new ClusterFanOut<StartUnitTest, UnitTestResult>();
        public List<TestWorker> Workers = new List<TestWorker>();
        
        public IObservable<UnitTestResult> StartUnitTest(StartUnitTest cfg)
        {
            return Workflow.Fanout(cfg);
        }

        public IObservable<IRxn> Process(StartUnitTest @event)
        {
            return StartUnitTest(@event);
        }

        public void Dispose()
        {
            
        }

        public void OnDispose(IDisposable obj)
        {
         
        }
    }


    public class UnitTestAgent : IContainerPostBuildService, IDisposable
    {
        private readonly IUpdateServiceClient _testUpdateProvider;
        private static StartUnitTest testcfg;
        public IDisposable TestRunner { get; set; }

        public static Func<StartUnitTest, Action<IRxnLifecycle>, Action<IRxnLifecycle>> TestAgent = (cfg, d) =>
        {
            UnitTestAgent.testcfg = cfg;
            return dd =>
            {
                d(dd);
                dd.CreatesOncePerApp<UnitTestAgent>();
            };
        };
        
        public static IObservable<IRxnAppContext> RunTestAgentReactor(string url = "http://192.168.1.2:888/", params string[] args)
        {
            return Rxns.Rxn.Create<IRxnAppContext>(o =>
            {
                // if (args.Contains("reactor"))
                // {
                //     OutOfProcessFactory.CreateNamedPipeClient(args.SkipWhile(a => a != "reactor").Skip(1).FirstOrDefault() ?? "spare");
                // }
                // else
                // {
                //     OutOfProcessFactory.CreateNamedPipeServer();
                // }

                //setup static object.Serialise() & string.Deserialise() methods
                RxnExtensions.DeserialiseImpl = (t, json) => JsonExtensions.FromJson(json, t);
                RxnExtensions.SerialiseImpl = (json) => JsonExtensions.ToJson(json);

                var cfg = RxnAppCfg.Detect(args);
                var testCfg = Program.TestCfg.Detect();
                
                var dll = args.Skip(0).FirstOrDefault().IsNullOrWhiteSpace(testCfg.Dll);// ??  "/Users/janison/replay/windows/JanisonReplay.Integration.Tests/bin/Debug/netcoreapp3.1/JanisonReplay.Integration.Tests.dll"; 
                var testName = args.Skip(1).FirstOrDefault().IsNullOrWhiteSpace(testCfg.RunThisTest);
                var appUpdateDllSource = args.Skip(2).FirstOrDefault().IsNullOrWhiteSpace(testCfg.UseAppUpdate);
                var appUpdateVersion = args.Skip(3).FirstOrDefault().IsNullOrWhiteSpace(testCfg.UseAppVersion);
                url = args.Skip(4).FirstOrDefault().IsNullOrWhiteSpace(url).IsNullOrWhiteSpace(testCfg.AppStatusUrl).IsNullOrWhiteSpace(cfg.AppStatusUrl);


                if (dll.IsNullOrWhitespace())
                {
                    o.OnError(new Exception($"'{dll}','{testName}','{appUpdateDllSource}','{appUpdateVersion}','{url}'\\r\\n" +
                                            "Usage: {testDll} {testName|all} {appUpdateTestDllSource} {appUpdateTestDllSourceVersion} {appStatusUrl}"));
                    return Disposable.Empty;
                }
                
                return UnitTestAgent.TestAgent(new StartUnitTest()
                    {
                        UseAppUpdate = appUpdateDllSource, 
                        UseAppVersion = appUpdateVersion,
                        Dll = dll, 
                        RunThisTest = testName
                    }, RxnApp.SpareReator(url))
                    .ToRxns()
                    .Named(new ClusteredAppInfo("DotNetTestWorker", "1.0.0", args, false))
                    .OnHost(new ConsoleHostedApp(), cfg)
                    .SelectMany(h => h.Run())
                    .Do(app =>
                    {
                        $"Heartbeating to {url}".LogDebug();
                        app.RxnManager.Publish(new PerformAPing()).Until();

                        $"Streaming logs".LogDebug();
                        app.RxnManager.Publish(new StreamLogs(TimeSpan.FromMinutes(60))).Until();
                    })
                    .Subscribe(o);
            });
        }

        public UnitTestAgent(IUpdateServiceClient testUpdateProvider)
        {
            _testUpdateProvider = testUpdateProvider;
        }
        
        public void Run(IReportStatus logger, IResolveTypes container)
        {
            logger.OnInformation("Starting unit test agent");

            Start();
            //todo: need to fix ordering of services, this needs to start before the appstatusservicce otherwise it will miss the infoprovdiderevent
            container.Resolve<SystemStatusPublisher>().Process(new AppStatusInfoProviderEvent()
            {
                Info = _info
            }).Until();
        }

        private Func<object> _info = () => null;
        private DateTime _started;

        private void Start()
        {
            var unitTestToRun = testcfg;
            _started = DateTime.Now;
            
            var testCluster = new UnitTesterCluster();
            var testWorker = new TestWorker("TestWorker#1", "local", _testUpdateProvider);
            
            testCluster.Process(new WorkerDiscovered<StartUnitTest, UnitTestResult>(){ Worker = testWorker }).WaitR();
            testCluster.Queue(unitTestToRun);

            BoardcastStatsToAppStatus(testCluster, unitTestToRun);

        }

        private void BoardcastStatsToAppStatus(UnitTesterCluster testCluster, StartUnitTest unitTestToRun)
        {
            _info = () => new
            {
                Test = $"Running {(unitTestToRun.RunAllTest ? "All" : unitTestToRun.RunThisTest)}",
                Duration = (DateTime.Now - _started).TotalMilliseconds,
                Workers = testCluster.Workers.Count
            };
            _info();
        }

        public void Dispose()
        {
            TestRunner.Dispose();
        }
    }
    
    

}
