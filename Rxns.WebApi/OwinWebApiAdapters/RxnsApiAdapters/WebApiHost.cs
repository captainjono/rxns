using System;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Microsoft.Owin.Security.OAuth;
using Rxns.DDD;
using Rxns.Health.AppStatus;
using Rxns.Hosting;
using Rxns.Logging;
using Rxns.Microservices;
using Rxns.WebApi.MsWebApiAdapters;
using Rxns.WebApi.OwinWebApiAdapters;

namespace Rxns.WebApi
{
    public class WebApiHost : ReportsStatus, IRxnHost, IRxnHostReadyToRun
    {
        private readonly IWebApiAdapter _webApiImpl;
        private readonly IWebApiCfg _cfg;
        private IRxnHostableApp _app;
        private IRxnAppCfg _rxnCfg;

        public WebApiHost(IWebApiCfg cfg, IWebApiAdapter webApiImpl)
        {
            _cfg = cfg;
            _webApiImpl = webApiImpl;
        }

        public IDisposable Start()
        {
            return this.ReportToDebug();
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
            return Rxn.Create<IRxnHostReadyToRun>(() =>
            {
                try
                {
                    app.Definition.UpdateWith(lifecycle =>
                    {
                        lifecycle
                            //.CreatesOncePerApp<AutofacTypeResolver>()
                            .Includes<AppStatusClientModule>()
                            .Includes<RxnsModule>()
                            .Includes<AppStatusServerModule>()//server modules always after client module
                            .Includes<DDDServerModule>()
                            //so adapters can be swapped out
                            .Includes<OwinWebApiAdapterModule>()
                            .CreatesOncePerApp(_ => cfg ?? RxnAppCfg.Detect(new string[0]))
                            .CreatesOncePerApp(_ => app)
                            .CreatesOncePerApp(_ => _cfg);
                    });

                    app.Definition.Build();

                    _app = app;

                    return this;
                }
                catch (Exception e)
                {
                    ReportStatus.Log.OnError($"App terminated unexpectedly", e);
                    Console.Error.WriteLine($"App terminated unexpectedly with: {e}");
                    Environment.Exit(1969);

                    return null;
                }
            });
        }

        public string Name { get; set; } = "WebApi2";
        public IObservable<IRxnAppContext> Run(IAppContainer container = null)
        {
            IDisposable endWs = Disposable.Empty;

            var finalContainer = container ?? _app.Container;

            return _app.Start().Select(context =>
                {
                    _webApiImpl.StartWebServices(_cfg, finalContainer, finalContainer.Resolve<OAuthAuthorizationServerProvider>(), null, true, reporter: finalContainer).DisposedBy(context);

                    return context;
                });
        }
    }
}
