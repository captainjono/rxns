using System;
using System.Reactive;
using System.Threading.Tasks;
using Autofac.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rxns.Hosting;
using Rxns.Interfaces;
using Rxns.Logging;
using Rxns.Microservices;

namespace Rxns.WebApiNET5.NET5WebApiAdapters
{

    public class AspnetCoreOnReadyHandler : IContainerPostBuildService
    {
        public IObservable<Unit> Run(IReportStatus logger, IResolveTypes container)
        {
            return Rxn.Create(() =>
            {
                container.Resolve<IAspnetCoreCfg>()?.OnReady(container.Resolve<IAppContainer>());
            });
        }
    }

    public class AspNetCoreWebApiAdapter
    {
        public AspNetCoreWebApiAdapter()
        {
        }

        /// <summary>
        /// todo: cleanup/refine this abstraction and implement auth and ensure cfg can be loaded from disk/to be fully customised by consumer as they would expect from vanlia aspnet impl
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="cfg"></param>
        /// <param name="enableErrorMessages"></param>
        /// <param name="encryptionKey"></param>
        /// <param name="reporter"></param>
        /// <returns></returns>
        public static async Task<IDisposable> StartWebServices<T>(IWebApiCfg cfg , params string[] args/*IOAuthAuthorizationServerProvider authProvider, IAuthenticationTokenProvider refreshProvider,*/ )
            where T : ConfigureAndStartAspnetCore
        {
            Action stopServer = () => { };
            try
            {

                var host = Host.CreateDefaultBuilder(args)
                    .UseEnvironment("Development")
                    .UseServiceProviderFactory(new AutofacServiceProviderFactory())
                    .ConfigureLogging((a, l) => { l.ClearProviders(); l.AddProvider(new RxnsLogDebugProvider()); l.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Information); })
                    .ConfigureWebHostDefaults(webHostBuilder =>
                    {
                        webHostBuilder
                            .UseUrls(cfg.ResolveBindingUrl().Split(','))
                            .UseContentRoot(cfg.Html5Root)
                            // WebRoot defaults to <contentRoot>/wwwroot — but our
                            // Html5Root IS the static-asset root (e.g. .../Web/dist),
                            // so the default probe for a non-existent /wwwroot
                            // subdir trips a noisy StaticFileMiddleware warning.
                            // Pin the WebRoot to Html5Root to silence it. No
                            // behaviour change — UseFileServer still serves from
                            // Html5Root directly via its own FileProvider.
                            .UseWebRoot(cfg.Html5Root)
                            .CaptureStartupErrors(true)
                            .UseKestrel(opts => {
                                // Default Kestrel cap is 30 MB. 250 MB matches the
                                // per-route RequestSizeLimit attribute on
                                // UpdatesController.Upload + the
                                // MultipartFormDataUploadProvider cap.
                                opts.Limits.MaxRequestBodySize = 250L * 1024 * 1024;

                                // Bind directly to a socket handle or Unix socket
                                // opts.ListenHandle(123554);
                                // opts.ListenUnixSocket("/tmp/kestrel-test.sock");
                                //opts.Listen(IPAddress.Loopback, port: 5002);
                                //opts.ListenAnyIP(869);
                                //opts.ListenLocalhost(5004, opts => opts.UseHttps());
                                //opts.ListenLocalhost(5005, opts => opts.UseHttps());
                            })
                            .UseStartup<T>();
                    }).Build();

                await host.RunAsync();
                
                stopServer = () =>
                {
                    "Stopping api on purpose".LogDebug();
                    host.Dispose();
                };

                return new DisposableAction(() =>
                {
                    ReportStatus.Log.OnWarning("Shutting down host");

                    stopServer();
                });
            }
            catch (Exception e)
            {
                ReportStatus.Log.OnError(e, "Webservices cannot be started");
                return null;
            }
        }
        }

}
