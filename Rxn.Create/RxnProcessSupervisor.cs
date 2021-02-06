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
            return waitBeforeRespawning.In().Do(_ =>
            {
                var p = Process.GetProcessesByName(programName).FirstOrDefault();
                
                if(p?.HasExited ?? true)
                    respawn();
            }).Until(ReportStatus.Log.OnError);
        }
        
        /// <summary>
        /// janison@janmac2 netcoreapp3.1 % dotnet Rxn.Create.dll NewAppUpdate "ReplayMacOS" "Latest-1." "/Users/janison/replay/windows/JanisonReplay.macOS/bin/Debug/JanisonReplay.app" false "http://192.168.1.77:888/"
        ///janison@janmac2 netcoreapp3.1 % dotnet rxn.create FromAppUpdate "ReplayMacOS" "Latest" "Contents/MacOS/JanisonReplay" "JanisonReplay.app" false "http://192.168.1.77:888/"
        /// janison@janmac2 netcoreapp3.1 % dotnet rxn.create.dll ReactorFor "/Users/janison/replay/windows/JanisonReplay.macOS/bin/Debug/JanisonReplay.app/Contents/MacOS/JanisonReplay" "http://192.168.1.77:888/"
        /// 
        /// </summary>
        /// <param name="pathToProcess"></param>
        /// <param name="version"></param>
        /// <param name="url"></param>
        /// <param name="args"></param>
        /// <returns></returns>
        public static IObservable<IRxnAppContext> RunSupervisorReactor(string pathToProcess, string version, string url = "http://localhost:888/", params string[] args)
        {
            return Rxns.Rxn.Create<IRxnAppContext>(o =>
            {
                $"Running supervisor for {pathToProcess}".LogDebug();

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
                
                return ProcessSupervisor.ReportOnProcess(GetProgramName(pathToProcess))(RxnApp.SpareReator(url))
                    .ToRxns(ProcessSupervisor.Supervise(pathToProcess), args)
                    .Named(new ClusteredAppInfo(GetProgramName(pathToProcess), version, args, false))
                    .OnHost(new ConsoleHostedApp(), cfg)
                    .Do(app =>
                    {
                        $"Advertising to {url}".LogDebug();
                        app.RxnManager.Publish(new PerformAPing()).Until(ReportStatus.Log.OnError);
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