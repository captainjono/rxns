﻿using System;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Rxns.DDD;
using Rxns.Health.AppStatus;
using Rxns.Hosting;
using Rxns.Logging;
using Rxns.Microservices;

namespace Rxns.WebApiNET5.NET5WebApiAdapters.RxnsApiAdapters
{
    public class WebApiHost : ReportsStatus, IRxnHostReadyToRun
    {
        private IWebApiCfg _cfg;
        private IRxnHostableApp _app;
        private IRxnAppCfg _appCfg;

        /// <summary>
        /// This requires the use of AspNetCoreWebApiAdapter.StartWebServices() as a boostrapper.
        /// this class then configures the boostrapper
        /// </summary>
        /// <param name="cfg"></param>
        public WebApiHost(IWebApiCfg cfg)
        {
            _cfg = cfg;
        }

        public IDisposable Start()
        {
            return Disposable.Empty;
        }

        public void Restart(string version = null)
        {
            "Restarting not supported on webapi host".LogDebug();
        }

        public IObservable<Unit> Install(string installer, string version)
        {
            return new Unit().ToObservable();
        }

        public void Restart()
        {
            "MigrateTo not implemented".LogDebug();
        }

        public IObservable<IRxnHostReadyToRun> Stage(IRxnHostableApp app, IRxnAppCfg cfg)
        {
            return Rxn.Create(() =>
            {

                //dont build on webapi, it does it for us
                //app.Definition.Build();

                _app = app;
                _appCfg = cfg;

                return this;
            });
        }

        public string Name { get; set; } = "DotNet5";

        public IObservable<IRxnAppContext> Run(IAppContainer container = null)
        {
            return Observable.Create<IRxnAppContext>(o =>
            {
                var endWs = Disposable.Empty;
                try
                {
                    _app.Definition.UpdateWith(lifecycle =>
                    {
                        lifecycle
                            .CreatesOncePerApp(_ => _appCfg)
                            .CreatesOncePerApp(_ => _app)
                            .CreatesOncePerApp(_ => _cfg);
                    });

                    _app.Definition.Build(container);

                    return _app.Start(true, container).FinallyR(() => { endWs.Dispose(); }).Subscribe(o);

                }
                catch (Exception e)
                {
                    Console.Error.WriteLine($"App terminated unexpectedly with: {e}".LogDebug("WS FATAL"));
                    Environment.Exit(1969);

                    return Disposable.Empty;
                }
            });
        }
    }
}
    
