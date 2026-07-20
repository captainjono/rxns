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
using Rxns.Microservices;
using Rxns.WebApiNET5.NET5WebApiAdapters.RxnsApiAdapters;
using System.Web.Http;   // WebApiExtensions — was nested under NET5WebApiAdapters before the shadowing fix

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
        Action<IAppContainer> OnReady { get; set; }
    }

    public class AspnetCoreCfg : IAspnetCoreCfg
    {
        public Action<IApplicationBuilder> Cfg { get; set; }
        public Action<IAppContainer> OnReady { get; set; } = a => { };
    }

    public abstract class ConfigureAndStartAspnetCore : IRxnAppDef
    {
        public abstract Func<string, Action<IRxnLifecycle>> App { get; }
        public abstract IRxnAppInfo AppInfo { get; }
        public abstract IWebApiCfg WebApiCfg { get; }

        /// <summary>
        /// Assemblies (in addition to <see cref="ConfigureAndStartAspnetCore"/>'s
        /// own and the host app's) whose controllers should be registered with MVC.
        /// Augment projects that ship controllers (e.g.
        /// <c>YourApp.Infra.YourController</c>) add their assembly
        /// here before <c>AppStatusPortal.StartAsync</c> so MVC's
        /// <see cref="ApplicationPartManager"/> picks them up. Without this hook,
        /// augment controllers are silently absent because MVC only scans the
        /// entry assembly + Rxns.WebApiNET5 by default.
        /// </summary>
        public static System.Collections.Generic.List<System.Reflection.Assembly> ExtraControllerAssemblies { get; }
            = new System.Collections.Generic.List<System.Reflection.Assembly>();

        public ConfigureAndStartAspnetCore()
        {
        }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection s)
        {
            // AddApplicationPart so controllers shipped INSIDE this assembly
            // (AppStatusLogController, ReportsStatusApiController, etc.) are
            // discovered when the consuming host's entry assembly doesn't directly
            // reference them. Without this, controllers in Rxns.WebApiNET5.dll are
            // silently absent from any consumer whose Startup-equivalent lives in a
            // different assembly (e.g. Rxns.AppStatus.Host).
            var builder = s.AddControllers()
                .AddApplicationPart(typeof(ConfigureAndStartAspnetCore).Assembly);
            // Also register the host app's assembly (caller-defined IRxnAppDef
            // subclass) — controllers shipped in there (e.g. AppInsightsController
            // in Rxns.AppStatus.Host) are picked up because of this. Then layer in
            // any augment assemblies registered via ExtraControllerAssemblies.
            builder.AddApplicationPart(GetType().Assembly);
            foreach (var asm in ExtraControllerAssemblies)
            {
                if (asm != null) builder.AddApplicationPart(asm);
            }
            s.AddRouting()
            .AddSignalR(o =>
                {
                    o.EnableDetailedErrors = true;
                })
            .AddJsonProtocol(o =>
            {
                // camelCase: the TestArena Angular client (appStatus.js, remoteShell.js,
                // eventHubService.js) reads payloads with lowercase property names
                // (tenant, systems, system, systemName, meta, ...). Server-side anonymous
                // objects + SystemStatusEvent POCOs are PascalCase, so without this policy
                // System.Text.Json serializes PascalCase on the wire and the JS lookups
                // silently miss every key -> #/appStatusV2 (and others) render blank.
                o.PayloadSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
                o.PayloadSerializerOptions.PropertyNameCaseInsensitive = true;
                o.PayloadSerializerOptions.AllowTrailingCommas = true;
            })
            .AddHubOptions<EventsHub>(c =>
            {

                c.EnableDetailedErrors = true;
                c.KeepAliveInterval = TimeSpan.FromSeconds(20);
                // Default 32 KB is too small for batched IRxn publishes —
                // a single PublishBatch from a chatty cmd (cat 500 KB,
                // RemoteShellPartialResult floods) trivially exceeds it
                // and the server tears down the whole connection. 4 MB
                // gives the bridge headroom even with Newtonsoft
                // type-info overhead per IRxn.
                c.MaximumReceiveMessageSize = 4 * 1024 * 1024;
                $"AddHubOptions<EventsHub>: MaximumReceiveMessageSize set to {c.MaximumReceiveMessageSize} bytes".LogDebug();

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
            var rxnApp = rxnAppDef.App(rxnAppDef.WebApiCfg.LocalUrl());
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
                    // See EventsHub mapping below for the three-limits explanation.
                    o.ApplicationMaxBufferSize = 4 * 1024 * 1024;
                    o.TransportMaxBufferSize   = 4 * 1024 * 1024;
                });
                endpoints.MapHub<EventsHub>("/eventsHub", o =>
                {
                    o.Transports =
                        HttpTransportType.WebSockets |
                        HttpTransportType.LongPolling;
                    // SignalR has THREE size limits and AddHubOptions only covers one:
                    //   1. HubOptions.MaximumReceiveMessageSize       (set in AddHubOptions, 4 MB)
                    //   2. HttpConnectionDispatcherOptions.ApplicationMaxBufferSize  (default 32 KB)
                    //   3. HttpConnectionDispatcherOptions.TransportMaxBufferSize    (default 32 KB)
                    // (2) and (3) are the per-connection transport buffers — when a
                    // PublishBatch frame exceeds them, the server tears down the
                    // connection with "InvalidDataException: maximum message size of
                    // 32768B was exceeded" before HubOptions even sees the frame.
                    o.ApplicationMaxBufferSize = 4 * 1024 * 1024;
                    o.TransportMaxBufferSize   = 4 * 1024 * 1024;
                });

                endpoints.MapHub<SystemMetricsHub>("/systemMetricsHub", o =>
                {
                    o.Transports =
                        HttpTransportType.WebSockets |
                        HttpTransportType.LongPolling;
                    // See EventsHub mapping above for the three-limits explanation.
                    o.ApplicationMaxBufferSize = 4 * 1024 * 1024;
                    o.TransportMaxBufferSize   = 4 * 1024 * 1024;
                });

                endpoints.MapHub<AppStatusLogHub>("/appStatusLogHub", o =>
                {
                    o.Transports =
                        HttpTransportType.WebSockets |
                        HttpTransportType.LongPolling;
                    // Mirror EventsHub's 4 MB buffers — log volume on chatty reporters
                    // can spike, and we don't want SignalR to tear down the portal
                    // connection on a per-frame size limit.
                    o.ApplicationMaxBufferSize = 4 * 1024 * 1024;
                    o.TransportMaxBufferSize   = 4 * 1024 * 1024;
                });
            });
            

            //webCfg = webCfg.UseAttributeRouting()
            //    .ResolveControllersWith(container)
            //    .LogErrorsWith(container);

            if (!cfg.Html5Root.IsNullOrWhitespace())
            {
                $"Enabling http file server @ {cfg.Html5Root}".LogDebug();

                var rxnsPortalRoot = new PhysicalFileProvider(cfg.Html5Root.EnsureRooted());
                var rxnsPortal = new FileServerOptions
                {
                    EnableDefaultFiles = true,
                    EnableDirectoryBrowsing = false,
                    FileProvider = rxnsPortalRoot,
                    StaticFileOptions = { FileProvider = rxnsPortalRoot, ServeUnknownFileTypes = true },
                    DefaultFilesOptions = { DefaultFileNames = new[] { "index.html", } }
                };

                server
                    .UseStaticFiles()
                    .UseFileServer(rxnsPortal);
            }

            // Augment overlay: if the host's cfg implements IAugmentableCfg and
            // supplies an AugmentRoot folder, mount it at /augment/*. The base
            // SPA's index.html loads /augment/init.js with onerror=this.remove()
            // so hosts without an augment cfg silently no-op. The interface lets
            // us bridge Rxns.WebApiNET5 (no awareness of host-specific cfg
            // classes) and host-side cfg (e.g. AppStatusHostCfg in Rxns.AppStatus.Host)
            // without a circular package reference.
            var augmentRoot = (cfg as Rxns.Hosting.IAugmentableCfg)?.AugmentRoot;
            if (!augmentRoot.IsNullOrWhitespace() && System.IO.Directory.Exists(augmentRoot))
            {
                $"Enabling augment overlay @ {augmentRoot} -> /augment".LogDebug();
                var augProvider = new PhysicalFileProvider(augmentRoot.EnsureRooted());
                server.UseFileServer(new FileServerOptions
                {
                    EnableDefaultFiles = false,
                    EnableDirectoryBrowsing = false,
                    RequestPath = "/augment",
                    FileProvider = augProvider,
                    StaticFileOptions = { FileProvider = augProvider, ServeUnknownFileTypes = true }
                });
            }

            //  .AllowCrossDomain()
            // .Use<TokenInQueryStringToAuthorizationHeaderMiddleware>()


            //if (allowErrors)
            //{
            //    webCfg.PassthroughErrors();
            //    hubConfig.PassthroughErrors();
            //}

            //webCfg.EnableCompression(); //handle gzip streams
            //via middleware

            foreach (var c in userCfg)
                c?.Cfg?.Invoke(server);

            //the order here is important, you must set it before using the webapi
            //otherwise the controllers wont recognise the tokens and [Authorize] will fail
            server
                .AllowCrossDomain();

            //.WithAuthentication(authProvider, refreshProvider, encryptionKey)
            //.MapSignalRWithCrossDomain(hubConfig, authProvider, refreshProvider, encryptionKey);
            //.RequireSsl()

        }
    }
}