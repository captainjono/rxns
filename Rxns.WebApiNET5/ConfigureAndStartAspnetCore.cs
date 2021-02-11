using System;
using System.Reactive.Linq;
using Autofac;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Rxns.Autofac;
using Rxns.Hosting;
using Rxns.Logging;
using Rxns.Microservices;
using Rxns.WebApiNET5.NET5WebApiAdapters;
using Rxns.WebApiNET5.NET5WebApiAdapters.RxnsApiAdapters;
using Rxns.WebApiNET5.NET5WebApiAdapters.System.Web.Http;

namespace Rxns.WebApiNET5
{
    public interface IWebApiRxn
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

    public abstract class ConfigureAndStartAspnetCore : IWebApiRxn
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
            s.AddRouting();
            s.AddSignalRCore()
                .AddJsonProtocol(j => { })
                .AddHubOptions<EventsHub>(c =>
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
            var rxnApp = App(WebApiCfg.BindingUrl);
            var appInfo = AppInfo; 
            var webApiHost = new WebApiHost(WebApiCfg);

            //var consoleHost = new ConsoleHostedApp(); // for unit/testing
            //var reliableHost = new RxnSupervisorHost(...); //will automatically reboot your app on failure. "always on"

            var readyToRun = cb
                .ToRxnsSupporting(rxnApp)
                .Named(appInfo)
                .OnHost(webApiHost, new RxnAppCfg());

            cb.Register(c =>
            {
                var cc = c.Resolve<IComponentContext>();
                var container = cc.Resolve<IContainer>();
                return new RxnStarter(() =>
                {
                    "Launching App in WebApi host".LogDebug();

                    
                    readyToRun // the apps supervisor
                        .SelectMany(h => h.Run(new AutofacAppContainer(container)))
                        .Do(rxnAppContext => { "App started".LogDebug(); })
                        .Until();
                });
            }).AsImplementedInterfaces().SingleInstance();



        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public virtual void Configure(IApplicationBuilder server, IWebHostEnvironment env)
        {

            var cfg = (IWebApiCfg)server.ApplicationServices.GetService(typeof(IWebApiCfg));
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

            // server.UseHttpsRedirection();
            server.UseRouting();

            //server.UseAuthentication();
            //server.UseAuthorization();

            server.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
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
            var rxnsPortalRoot = new PhysicalFileProvider(cfg.Html5Root);
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