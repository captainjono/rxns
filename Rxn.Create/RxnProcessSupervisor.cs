using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Autofac;
using Rxns;
using Rxns.Health;
using Rxns.Hosting;
using Rxns.Logging;

namespace RxnCreate
{
    public class RxnProcessSupervisor
    {
        
        public static IObservable<IDisposable> SupervisedProcess(string pathToProcess)
        {
            return Rxn.Create(pathToProcess, null, i => ReportStatus.Log.OnInformation(pathToProcess, i), e => ReportStatus.Log.OnError(pathToProcess, e));
        }


        private static IDisposable RestartIfDoesntRespawnAutomatically(string programName, TimeSpan waitBeforeRespawning, Func<Process> respawn)
        {
            return waitBeforeRespawning.Then().Do(_ =>
            {
                var p = Process.GetProcessesByName(programName).FirstOrDefault();
                
                if(p?.HasExited ?? true)
                    respawn();
            }).Until();
        }
        
        /// <summary>
        /// Creates a supervisor that will then allow you to spawn child processes and address them with a centalised rxnmanager.
        /// </summary>
        /// <param name="systemAppPath"></param>
        /// <param name="version"></param>
        /// <param name="url"></param>
        /// <param name="args"></param>
        /// <returns></returns>
        public static IObservable<IRxnAppContext> RunSupervisorReactor(string systemAppPath, string version, string url = "http://localhost:888/", params string[] args)
        {
            return Rxns.Rxn.Create<IRxnAppContext>(o =>
            {
                $"Running supervisor for {systemAppPath}".LogDebug();

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
                
                return ProcessSupervisor.ReportOnProcess(GetProgramName(systemAppPath))(RxnApp.SpareReator(url))
                    .ToRxns(ProcessSupervisor.Supervise(systemAppPath), args)
                    .Named(new ClusteredAppInfo(GetProgramName(systemAppPath), version, args, false))
                    .OnHost(new ConsoleHostedApp(), cfg)
                    .SelectMany(h => h.Run())
                    .Do(app =>
                    {
                        $"Advertising to {url}".LogDebug();
                        app.RxnManager.Publish(new PerformAPing()).Until();
                    })
                    .Subscribe(o);
            });
        }
        
        public  static Process StartProcess(string pathToProcess)
        {
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
            
            "Setup exit handler".LogDebug();
            p.Exited += (sender, args) =>
            {
                ReportStatus.Log.OnWarning($"{pathToProcess} exited, restarting");

                RestartIfDoesntRespawnAutomatically(pathToProcess, TimeSpan.FromSeconds(10), () => StartProcess(pathToProcess));
                //o.OnCompleted();
            };

            var nameOfProcess = new FileInfo(pathToProcess).Name;

            p.Start();
                
            Rxn.DfrCreate(() => p.StandardOutput.ReadLineAsync().ToObservable().Do(msg =>
                {
                    msg.LogDebug(nameOfProcess);
                }))
                .DoWhile(() => !p.HasExited)
                .Until(ReportStatus.Log.OnError);

            Rxn.DfrCreate(() => p.StandardError.ReadLineAsync().ToObservable().Do(msg =>
                {
                    ReportStatus.Log.OnInformation($"[{nameOfProcess}{msg}");
                }))
                .DoWhile(() => !p.HasExited)
                .Until(ReportStatus.Log.OnError);
            
            p.StandardError.ReadToEndAsync().ToObservable().Do(msg =>
                {
                    ReportStatus.Log.OnError(nameOfProcess, msg);
                })
                .DoWhile(() => !p.HasExited)
                .Until(ReportStatus.Log.OnError);
        

            return p;
        }
        
        
        public static class ProcessSupervisor
        {
            public static Func<string, Func<Action<IRxnLifecycle>, Action<IRxnLifecycle>>> ReportOnProcess => name => d =>
            {
                return dd =>
                {
                    d(dd);    
                    dd.CreatesOncePerApp(_ => new MacOSCrashLogger(new AppVersionInfo(name, "", true)));
                };
            };
            
            public static IObservable<IDisposable> Supervise(string pathToProcess)
            {
                return Rxn.Create<IDisposable>(o =>
                {
                    $"Starting {pathToProcess}".LogDebug();

                    var p = StartProcess(pathToProcess);
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
        }

        private static string GetProgramName(string pathToExe)
        {
            return pathToExe.Split(new[] {'/', '\\'}).LastOrDefault();
        }
    }
}