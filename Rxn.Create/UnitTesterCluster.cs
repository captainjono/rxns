using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Rxns;
using Rxns.DDD.Commanding;
using Rxns.Hosting;
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
    }

    public class UnitTestResult : CommandResult
    {
        public bool WasSuccessful { get; set; }
    }

    public class TestWorker
    {
        public string Name { get; }
        public string Route { get; }

        public TestWorker(string name, string route)

        {
            Name = name;
            Route = route;
        }

        public IObservable<UnitTestResult> DoWork(StartUnitTest work)
        {
            var file = File.Create($"testLog_{DateTime.Now.ToString("s").Replace(":", "")}");
            var testLog = new StreamWriter(file, leaveOpen: true);
            

            return Rxn.Create("dotnet", $"test{FilterIfSingleTestOnly(work)} {work.Dll}", i => testLog.WriteLine(i.LogDebug(work.RunThisTest ?? work.Dll)), e => testLog.WriteLine(e.LogDebug(work.RunThisTest ?? work.Dll)))
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
            return work.RunThisTest.IsNullOrWhitespace() ? "" : $" -filter Name={work.RunThisTest}";
        }
    }

    public class ClusterFanOut
    {
        private IDictionary<string, TestWorker> _agents = new Dictionary<string, TestWorker>();
        
        public IDisposable RegisterWorker(string name, string route)
        {
            _agents.Add(route, new TestWorker(name, route));
            return Disposable.Empty;
        }

        private int _lastWorkerIndex = 0;

        public IObservable<UnitTestResult> Fanout(StartUnitTest cfg) //todo make generic
        {
            if (_agents.Count >= _lastWorkerIndex)
            {
                _lastWorkerIndex = 0;
            }
            else
            {
                _lastWorkerIndex++;
            }

            var nextAgent = _agents.Skip(_lastWorkerIndex).FirstOrDefault().Value;

            "Sending work to {name}".LogDebug();

            return nextAgent.DoWork(cfg);
        }
    }

    public class UnitTesterCluster : IRxnProcessor<StartUnitTest>, IManageResources
    {
        public ClusterFanOut Workflow = new ClusterFanOut();

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


    public class UnitTestAgent : IContainerPostBuildService
    {
        public static Func<Action<IRxnLifecycle>, Action<IRxnLifecycle>> TestAgent = d =>
        {
            return dd =>
            {
                d(dd);
                dd.CreatesOncePerApp<UnitTestAgent>();
            };
        };

        public void Run(IReportStatus logger, IResolveTypes container)
        {
            logger.OnInformation("Starting unit test agent");

            Start();
        }

        private void Start()
        {
            var unitTestToRun = new StartUnitTest()
            {
                Dll = "JanisonReplay.Integration.Tests.dll",
                RunThisTest = "should_exit_gracefully_at_different_points_in_studentWorkflow",
            };

            $"Running {unitTestToRun.Dll} {(unitTestToRun.RunAllTest ? "All" : unitTestToRun.RunThisTest)}".LogDebug();

            var testCluster = new TestWorker("JanisonReplayTester", "local");
            testCluster.DoWork(unitTestToRun).WaitR();
        }
    }

}
