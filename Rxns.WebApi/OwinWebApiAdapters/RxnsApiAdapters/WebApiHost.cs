using System;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Microsoft.Owin.Security.OAuth;
using Rxns.DDD;
using Rxns.Health.AppStatus;
using Rxns.Hosting;
using Rxns.Logging;
using Rxns.WebApi.MsWebApiAdapters;
using Rxns.WebApi.OwinWebApiAdapters;

namespace Rxns.WebApi
{
    public class WebApiHost : ReportsStatus, IRxnHost
    {
        private readonly IWebApiAdapter _webApiImpl;
        private readonly IWebApiCfg _cfg;

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

        public IObservable<IRxnAppContext> Run(IRxnHostableApp app, IRxnAppCfg cfg)
        {
            return Observable.Create<IRxnAppContext>(o =>
            {
                try
                {
                    IDisposable endWs = Disposable.Empty;

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
                            .CreatesOncePerApp(_ => cfg)
                            .CreatesOncePerApp(_ => _cfg);
                    });

                    app.Definition.Build();

                    return app.Start().Select(context =>
                    {
                        endWs = _webApiImpl.StartWebServices(_cfg, app.Container, app.Resolver.Resolve<OAuthAuthorizationServerProvider>(), null, true, reporter: app.Container);

                        return context;
                    })
                    .FinallyR(() =>
                    {
                        endWs.Dispose();
                    }).Subscribe();
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine($"App terminated unexpectedly with: {e}");
                    Environment.Exit(1969);

                    return Disposable.Empty;
                }
            });
        }

        public string Name { get; set; } = "WebApi2";
    }
}
