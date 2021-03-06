﻿using System;
using System.Reactive;
using System.Threading.Tasks;
using Autofac.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
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
                    //.ConfigureLogging((a, l) => l.AddConsole())//.AddDebug().SetMinimumLevel(LogLevel.Trace))
                    .ConfigureWebHostDefaults(webHostBuilder =>
                    {
                        webHostBuilder
                            .UseUrls(cfg.BindingUrl.Split(','))
                            .UseContentRoot(cfg.Html5Root)
                            .CaptureStartupErrors(true)
                            .UseKestrel(opts => {
                                
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
