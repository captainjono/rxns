using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Rxns.DDD;
using Rxns.DDD.Commanding;
using Rxns.DDD.CQRS;
using Rxns.Interfaces;
using Rxns.Logging;
using Rxns.Microservices;
using Rxns.NewtonsoftJson;

namespace Rxns.Hosting
{
    public class ConsoleHostedApp : ReportsStatus, IRxnHostReadyToRun
    {
        private IRxnAppCfg _cfg;
        private IRxnHostableApp _app;

        public IDisposable Start()
        {
            return Disposable.Empty;
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

        public IObservable<IRxnHostReadyToRun> Stage(IRxnHostableApp app, IRxnAppCfg cfg)
        {
            return Rxn.Create(() =>
            {
                _app = app;
                _cfg = cfg;

                app.Definition.UpdateWith(def =>
                {
                    def.CreatesOncePerApp(_ => this);
                    def.CreatesOncePerApp(_ => cfg);
                    def.CreatesOncePerApp(_ => app);
                });

                return this;
            });
        }

        public IObservable<IRxnAppContext> Run(IAppContainer container = null)
        {
            return Rxn.Create<IRxnAppContext>(o =>
            {
                try
                {
                

                    try
                    {
                        _app.Definition.Build(container);
                    }
                    catch (Exception e)
                    {
                        OnWarning($"On app build: {e}");
                    }

                    //todo: u
                    //need to add support for external process

                    return _app.Start(true, container).Do(c =>
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

        public static void StartREPL(IAppCommandService context, CancellationToken cancel = default)
        {
            StartREPL(context, "", null, cancel);
        }

        /// <summary>
        /// Prompt-based REPL over <paramref name="context"/>. Each line is dispatched via
        /// <c>IAppCommandService.ExecuteCommand(<paramref name="defaultRoute"/>, cmd)</c>,
        /// letting a host pre-pin the REPL to a specific remote target (e.g. a worker's
        /// <c>tenant\systemName</c> route) instead of always running commands locally.
        /// Optional <paramref name="wrapCmd"/> lets the host transform each typed line
        /// before dispatch (e.g. wrap raw shell input as a routed shell command).
        /// </summary>
        public static void StartREPL(IAppCommandService context, string defaultRoute, Func<string, string> wrapCmd, CancellationToken cancel = default)
        {
            StartREPL(context, defaultRoute, wrapCmd, getPrompt: null, cancel);
        }

        /// <summary>
        /// Same as the 4-arg overload but takes an optional <paramref name="getPrompt"/>
        /// callback so the host can render a dynamic prompt label (e.g. the current
        /// shell cwd) per iteration. Returns null/empty → falls back to the static
        /// route label.
        /// </summary>
        public static void StartREPL(IAppCommandService context, string defaultRoute, Func<string, string> wrapCmd, Func<string> getPrompt, CancellationToken cancel = default)
        {
            string staticLabel = string.IsNullOrEmpty(defaultRoute) ? ">" : $"{defaultRoute}$ ";
            Console.WriteLine(string.IsNullOrEmpty(defaultRoute)
                ? "Ready for commands:"
                : $"Ready for commands (default route: {defaultRoute}):");
            while (!cancel.IsCancellationRequested)
            {
                var dyn = getPrompt != null ? getPrompt() : null;
                var promptLabel = !string.IsNullOrEmpty(dyn) ? dyn : staticLabel;
                Console.Write(promptLabel);

                // Non-blocking read that respects cancellation
                var readTask = Task.Run(() => Console.ReadLine(), cancel);
                try { readTask.Wait(cancel); } catch (OperationCanceledException) { return; }

                // Task.IsCompletedSuccessfully was added in netstandard2.1 — use
                // TaskStatus.RanToCompletion for netstandard2.0 compatibility.
                var cmd = readTask.Status == TaskStatus.RanToCompletion ? readTask.Result : null;

                if (cmd == null || cmd == "exit" || cmd == "e")
                {
                    return;
                }

                var toDispatch = wrapCmd != null ? wrapCmd(cmd) : cmd;

                context.ExecuteCommand(defaultRoute, toDispatch).Do(r =>
                {
                    if(!(r is CommandResult))
                        Console.WriteLine($"-->\r\b\n{r.ToJson()}\r\n-->");
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
