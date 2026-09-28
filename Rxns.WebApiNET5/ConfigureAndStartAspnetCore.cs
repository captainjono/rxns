using System;
using System.Reactive.Linq;
using Autofac;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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
        /// <summary>
        /// The ceiling every hub message is allowed, in bytes.
        ///
        /// <para>SignalR enforces this in THREE independent places, and AddHubOptions only
        /// covers one of them. Their defaults differ, which is what makes a partial fix look like
        /// a complete one: HubOptions.MaximumReceiveMessageSize defaults to 32 KB (applied by
        /// HubOptionsSetup - the raw options object leaves it null), while the endpoint's
        /// ApplicationMaxBufferSize and TransportMaxBufferSize default to 64 KB each. Raise only
        /// the hub limit and the next ceiling is 64 KB, reached silently.</para>
        ///
        /// <para>The failure is hard to read from either. A message over the hub limit closes the
        /// connection naming the size; one over a buffer limit closes it without. Both then look
        /// like a reconnect in the log rather than a refusal. Named once here so the three cannot
        /// drift apart - they are only correct as a set.</para>
        /// </summary>
        internal const long HubMessageSizeLimitBytes = 4 * 1024 * 1024;

        /// <summary>
        /// The two limits AddHubOptions does not set.
        /// </summary>
        ///
        /// <para>Applied at MapHub, where they are easy to omit and impossible to notice
        /// missing: a hub with only MaximumReceiveMessageSize raised behaves correctly until a
        /// payload passes 64 KB, and then refuses every one of them. Nothing near that size
        /// occurs in development, so the gap only opens at fleet scale.</para>
        internal static void ApplyHubTransportLimits(HttpConnectionDispatcherOptions o)
        {
            o.ApplicationMaxBufferSize = HubMessageSizeLimitBytes;
            o.TransportMaxBufferSize   = HubMessageSizeLimitBytes;
        }

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
            s.AddRouting();
            AddRxnsSignalR(s);
        }

        /// <summary>
        /// Every SignalR registration, in one place a test can call.
        ///
        /// <para>Split out of ConfigureServices because the size limits it sets are only
        /// meaningful as RESOLVED options - an AddHubOptions delegate can run, log the value it
        /// assigned, and still lose to something registered after it, which is invisible at the
        /// call site and shows up only as a hub refusing a large message at scale. Testing that
        /// needs the real chain against a real container, not a copy of it here.</para>
        /// </summary>
        internal static void AddRxnsSignalR(IServiceCollection s)
        {
            s.AddSignalR(o =>
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
                // 45s between pings, and a client is not given up on for 120s.
                //
                // The pair has to hold ClientTimeout >= 2 x KeepAlive or a peer is declared dead
                // between pings under any load at all. It used to be a 20s ping against the 30s
                // default, which is 1.5x - and on a 100-device rung the arena took 33.3 SECONDS to
                // answer one request, so the leg carrying dispatch and results was severed nine
                // times over before it gave up. 120s is three and a half times that worst case.
                //
                // Fewer pings also matters at fleet scale: a ping every 20s across a hundred
                // connections is traffic the arena answers instead of doing work.
                c.KeepAliveInterval = TimeSpan.FromSeconds(45);
                c.ClientTimeoutInterval = TimeSpan.FromSeconds(120);
                // MaximumReceiveMessageSize is deliberately NOT set here - see the
                // PostConfigure below for why setting it in this delegate does not stick.
            })
            .AddHubOptions<SystemMetricsHub>(c =>
            {
                c.EnableDetailedErrors = true;
                c.KeepAliveInterval = TimeSpan.FromSeconds(45);
                c.ClientTimeoutInterval = TimeSpan.FromSeconds(120);
            })
            .AddHubOptions<ReportHub>(c =>
            {
                c.EnableDetailedErrors = true;
                c.KeepAliveInterval = TimeSpan.FromSeconds(45);
                c.ClientTimeoutInterval = TimeSpan.FromSeconds(120);
            });

            // PostConfigure, not the AddHubOptions delegate above, and this is not a style
            // preference.
            //
            // The default 32 KB is far too small for batched IRxn publishes - one PublishBatch
            // from a chatty command trivially exceeds it and the server tears the whole
            // connection down. So it gets raised. The catch is WHERE.
            //
            // AddHubOptions contributes one IConfigureOptions<HubOptions<THub>> among several;
            // SignalR's own HubOptionsSetup<THub> is another, and it copies the global 32 KB
            // default over whatever it finds. Which of the two wins is decided purely by the
            // order the container hands them to OptionsFactory. Under the plain ServiceCollection
            // that order puts ours last and the 4 MB sticks. This host does not serve from the
            // ServiceCollection - it calls UseServiceProviderFactory(new
            // AutofacServiceProviderFactory()), and Autofac's ordering puts HubOptionsSetup last
            // instead. The assignment ran, logged its 4 MB, and was then overwritten back to
            // 32768 before a single message was sized against it.
            //
            // IPostConfigureOptions is defined to run after EVERY IConfigureOptions, so this is
            // the one place the value cannot be undone by something registered elsewhere. It is
            // also why the arena's startup diagnostic has been correctly reporting 32768 for so
            // long while the log two lines above it said 4194304: both were true.
            // The floor for EVERY hub, including ones added later and ones defined in hosts that
            // cannot reach this method at all.
            //
            // IAspnetCoreCfg gives a host a hook on IApplicationBuilder and nothing on
            // IServiceCollection, so a host that maps its own hub can raise that hub's two
            // endpoint buffers at MapHub and has no way to raise the third limit. theBFG's
            // progress hub sat in exactly that state: both buffers set to 4 MB beside a comment
            // saying the matching AddHubOptions was "registered by ConfigureServices override",
            // and no such override anywhere - it could not have existed. The hub ran on the
            // 32 KB default, which is the ceiling that actually bites first.
            //
            // Setting the global rather than adding a per-hub registration is deliberate: the
            // repeated failure here is a hub whose limits nobody raised, and a floor removes that
            // possibility instead of relying on each new hub remembering. HubOptionsSetup<THub>
            // copies this into every per-hub HubOptions, so one value reaches all of them.
            s.PostConfigure<Microsoft.AspNetCore.SignalR.HubOptions>(o =>
            {
                o.MaximumReceiveMessageSize = HubMessageSizeLimitBytes;
                $"SignalR: MaximumReceiveMessageSize floor for all hubs pinned to {o.MaximumReceiveMessageSize} bytes".LogDebug();
            });

            s.PostConfigure<Microsoft.AspNetCore.SignalR.HubOptions<EventsHub>>(o =>
            {
                o.MaximumReceiveMessageSize = HubMessageSizeLimitBytes;
                $"EventsHub: MaximumReceiveMessageSize pinned to {o.MaximumReceiveMessageSize} bytes (post-configure, so nothing can overwrite it)".LogDebug();
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
                    ApplyHubTransportLimits(o);
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
                    // connection before HubOptions ever sees the frame. Note these
                    // default to 64 KB, not to the 32 KB that appears in
                    // "InvalidDataException: maximum message size of 32768B was
                    // exceeded" - that text comes from the hub limit above, which is a
                    // separate check. Both have to be raised; neither implies the other.
                    ApplyHubTransportLimits(o);
                });

                endpoints.MapHub<SystemMetricsHub>("/systemMetricsHub", o =>
                {
                    o.Transports =
                        HttpTransportType.WebSockets |
                        HttpTransportType.LongPolling;
                    // See EventsHub mapping above for the three-limits explanation.
                    ApplyHubTransportLimits(o);
                });

                endpoints.MapHub<AppStatusLogHub>("/appStatusLogHub", o =>
                {
                    o.Transports =
                        HttpTransportType.WebSockets |
                        HttpTransportType.LongPolling;
                    // Mirror EventsHub's 4 MB buffers — log volume on chatty reporters
                    // can spike, and we don't want SignalR to tear down the portal
                    // connection on a per-frame size limit.
                    ApplyHubTransportLimits(o);
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