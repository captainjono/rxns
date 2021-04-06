using System;
using System.Reactive.Linq;
using Autofac;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Rxns.Autofac;
using Rxns.Hosting;
using Rxns.Logging;
using Rxns.WebApiNET5.NET5WebApiAdapters.RxnsApiAdapters;
using Rxns.WebApiNET5.NET5WebApiAdapters.System.Web.Http;

namespace Rxns.WebApiNET5
{
    public interface IRxnAppDef
    {
        Func<string, Action<IRxnLifecycle>> App { get; }
        IRxnAppInfo AppInfo { get; }
        IWebApiCfg WebApiCfg { get; }
    }

    public class RxnStarter : IStartupFilter
    {
        private readonly Action _onStart;

        public RxnStarter(Action onStart)
        {
            _onStart = onStart;
        }
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {

            _onStart();
            return next;
        }
    }

    public interface IAspnetCoreCfg
    {
        Action<IApplicationBuilder> Cfg { get; }
    }

    public class AspnetCoreCfg : IAspnetCoreCfg
    {
        public Action<IApplicationBuilder> Cfg { get; set; }
    }

    public abstract class ConfigureAndStartAspnetCore : IRxnAppDef
    {
        public abstract Func<string, Action<IRxnLifecycle>> App { get; }
        public abstract IRxnAppInfo AppInfo { get; }
        public abstract IWebApiCfg WebApiCfg { get; }

        public ConfigureAndStartAspnetCore()
        {
        }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection s)
        {
            s.AddControllers();
            s.AddRouting()
            .AddSignalR(o =>
                {
                    o.EnableDetailedErrors = true;
                })
            .AddJsonProtocol(o =>
            {
                o.PayloadSerializerOptions.PropertyNameCaseInsensitive = true;
                o.PayloadSerializerOptions.AllowTrailingCommas = true;
            })
            .AddHubOptions<EventsHub>(c =>
            {

                c.EnableDetailedErrors = true;
                c.KeepAliveInterval = TimeSpan.FromSeconds(20);

            })
            .AddHubOptions<SystemMetricsHub>(c =>
            {
                c.EnableDetailedErrors = true;
                c.KeepAliveInterval = TimeSpan.FromSeconds(20);
            })
            .AddHubOptions<ReportHub>(c =>
            {
                c.EnableDetailedErrors = true;
                c.KeepAliveInterval = TimeSpan.FromSeconds(20);
            });

        }


        public void ConfigureContainer(ContainerBuilder cb)
        {
            CreateApp(cb, this).Until();
        }

        public static IObservable<IRxnHostReadyToRun> CreateApp(ContainerBuilder cb, IRxnAppDef rxnAppDef, string[] args = default(string[]))
        {
            var rxnApp = rxnAppDef.App("http://localhost:888");
            var appInfo = rxnAppDef.AppInfo;
            var webApiHost =  new WebApiHost(rxnAppDef.WebApiCfg);

            var appReadyToRun = cb
                .ToRxnsSupporting(rxnApp)
                .Named(appInfo)
                .OnHost(webApiHost, new RxnAppCfg() { Args = args });

            cb.Register(c =>
            {
                var cc = c.Resolve<IComponentContext>();
                var container = cc.Resolve<IContainer>();
                return new RxnStarter(() =>
                {
                    "Launching App in WebApi host".LogDebug();
                    
                    appReadyToRun // the apps supervisor
                        .SelectMany(h => h.Run(new AutofacAppContainer(container)))
                        .Do(rxnAppContext => { "App started".LogDebug(); })
                        .Until();
                });
            }).AsImplementedInterfaces().SingleInstance();

            //var consoleHost = new ConsoleHostedApp(); // for unit/testing
            //var reliableHost = new RxnSupervisorHost(...); //will automatically reboot your app on failure. "always on"


            return appReadyToRun;
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public virtual void Configure(IApplicationBuilder server, IWebHostEnvironment env)
        {
            var cfg = (IWebApiCfg)server.ApplicationServices.GetService(typeof(IWebApiCfg));
            var userCfg = (IAspnetCoreCfg[])server.ApplicationServices.GetService(typeof(IAspnetCoreCfg[]));
            //env.WebRootPath = cfg.Html5Root;
            //env.WebRootFileProvider =
            if (env.IsDevelopment())
            {
                server.UseDeveloperExceptionPage();
            }
            else
            {
                server.UseHsts();
            }

            server.UseDeveloperExceptionPage();


            // server.UseHttpsRedirection();
            server.UseRouting();


            //server.UseAuthentication();
            //server.UseAuthorization();

            server.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapHub<ReportHub>("/reportHub", o =>
                {
                    o.Transports =
                        HttpTransportType.WebSockets |
                        HttpTransportType.LongPolling;
                });
                endpoints.MapHub<EventsHub>("/eventsHub", o =>
                {
                    o.Transports =
                        HttpTransportType.WebSockets |
                        HttpTransportType.LongPolling;
                });

                endpoints.MapHub<SystemMetricsHub>("/systemMetricsHub", o =>
                {
                    o.Transports =
                        HttpTransportType.WebSockets |
                        HttpTransportType.LongPolling;
                });
            });


            //var webCfg = new HttpConfiguration();
            //if(_requestCfg != null)
            //{
            //    webCfg = _requestCfg(webCfg);
            //}
            //.RequireSsl()


            //webCfg = webCfg.UseAttributeRouting()
            //    .ResolveControllersWith(container)
            //    .LogErrorsWith(container);

            server.UseStaticFiles();
            var rxnsPortalRoot = new PhysicalFileProvider(cfg.Html5Root.EnsureRooted());
            var rxnsPortal = new FileServerOptions
            {
                EnableDefaultFiles = true,
                EnableDirectoryBrowsing = false,
                FileProvider = rxnsPortalRoot,
                StaticFileOptions = { FileProvider = rxnsPortalRoot, ServeUnknownFileTypes = true },
                DefaultFilesOptions = { DefaultFileNames = new[] { "index.html", } }
            };



            //  .AllowCrossDomain()
            // .Use<TokenInQueryStringToAuthorizationHeaderMiddleware>()


            //if (allowErrors)
            //{
            //    webCfg.PassthroughErrors();
            //    hubConfig.PassthroughErrors();
            //}

            //webCfg.EnableCompression(); //handle gzip streams
            //via middleware

            foreach(var c in userCfg)
                c?.Cfg(server);

            //the order here is important, you must set it before using the webapi
            //otherwise the controllers wont recognise the tokens and [Authorize] will fail
            server
                .AllowCrossDomain()
                //.WithAuthentication(authProvider, refreshProvider, encryptionKey)

                .UseFileServer(rxnsPortal);

            //.MapSignalRWithCrossDomain(hubConfig, authProvider, refreshProvider, encryptionKey);
        }
    }
}