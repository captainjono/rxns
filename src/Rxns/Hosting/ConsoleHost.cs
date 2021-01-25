using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Rxns.DDD;
using Rxns.DDD.Commanding;
using Rxns.DDD.CQRS;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns.Hosting
{
    public class ConsoleHostedApp : ReportsStatus, IRxnHost
    {
        private IRxnAppCfg _cfg;

        public IDisposable Start()
        {
            return this.ReportToDebug();
        }

        public void Restart(string version = null)
        {
            var processName = Process.GetCurrentProcess().ProcessName;
            
            if(!File.Exists(Path.Combine(Environment.CurrentDirectory, processName)))
                if (File.Exists(Path.Combine(Environment.CurrentDirectory, processName + ".exe")))
                {
                    processName = Path.Combine(Environment.CurrentDirectory, processName + ".exe");
                }

            Process.Start(new ProcessStartInfo()
            {
                FileName = processName,
                Arguments = _cfg.Args.ToStringEach(" "),
                WorkingDirectory = Environment.CurrentDirectory
            });

            Environment.Exit(0);
        }

        public IObservable<Unit> Install(string installerZip, string version)
        {
            return new Unit().ToObservable();
        }

        public IObservable<IRxnAppContext> Run(IRxnHostableApp app, IRxnAppCfg cfg)
        {
            _cfg = cfg;
            return Rxn.Create<IRxnAppContext>(o =>
            {
                try
                {
                    app.Definition.UpdateWith(def =>
                    {
                        def.CreatesOncePerApp(_ => this);
                        def.CreatesOncePerApp(_ => cfg);
                        def.CreatesOncePerApp(_ => app);
                    });

                    try
                    {
                        if(app.Container == null)
                            app.Definition.Build();
                    }
                    catch (Exception e)
                    {
                        OnWarning($"On app build: {e}");
                    }


                    return app.Start().Do(c =>
                    {
                        "saw context".LogDebug();
                        o.OnNext(c);
                    })
                    .LastOrDefaultAsync()
                    .FinallyR(() =>
                    {
                        "ended".LogDebug();
                        o.OnCompleted();
                    }).Subscribe();
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine($"App terminated unexpectedly with: {e}");
                    o.OnError(e);

                    return Disposable.Empty;
                }
            });
        }

        public string Name { get; set; } = "ConsoleHost";

        public static void StartREPL(IAppCommandService context)
        {
            Console.WriteLine("Ready for commands:");
            while (true)
            {


                //context.CmdService.Run(new LookupReactorCountQry()).Do(r =>
                //{
                //    Console.WriteLine($"-->\r\b\n{r}\r\n-->");
                //}).Until(e => Console.Error.WriteLine(e.Message));

                Console.Write(">");
                var cmd = Console.ReadLine();

                if (cmd == "exit" || cmd == "e")
                {
                    return;
                }

                var c = new LookupReactorCountQry();
                
                context.ExecuteCommand("",cmd).Do(r =>
                {
                    Console.WriteLine($"-->\r\b\n{r.ToString()}\r\n-->");
                }).Until(e => Console.Error.WriteLine(e.Message));
            }
        }
    }


    public class LookupReactorCount : ServiceCommand
    {
    }

    public class LookupReactorCountQry : TenantQry<int>
    {
    }
}
