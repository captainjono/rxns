using System;
using System.Threading.Tasks;
using Autofac.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Rxns.Hosting;
using Rxns.Logging;

namespace Rxns.WebApiNET5.NET5WebApiAdapters
{
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
        public static async Task<IDisposable> StartWebServices<T>(IWebApiCfg cfg/*IOAuthAuthorizationServerProvider authProvider, IAuthenticationTokenProvider refreshProvider,*/ )
            where T : ConfigureAndStartAspnetCore
        {
            Action stopServer = () => { };
            try
            {

                var host = Host.CreateDefaultBuilder(/*args*/)
                    .UseServiceProviderFactory(new AutofacServiceProviderFactory())
                    .ConfigureWebHostDefaults(webHostBuilder => {
                        webHostBuilder
                            .UseUrls(cfg.BindingUrl.Split(','))
                            .UseContentRoot(cfg.Html5Root)
                            .UseKestrel(opts =>
                            {
                                // Bind directly to a socket handle or Unix socket
                                // opts.ListenHandle(123554);
                                // opts.ListenUnixSocket("/tmp/kestrel-test.sock");
                                //opts.Listen(IPAddress.Loopback, port: 5002);
                                opts.ListenAnyIP(888);
                                //opts.ListenLocalhost(5004, opts => opts.UseHttps());
                                //opts.ListenLocalhost(5005, opts => opts.UseHttps());
                            })
                            .UseStartup<T>();
                    })
                    .Build();

                await host.RunAsync();

                //var configuredWebApi = Host.CreateDefaultBuilder()
                //    .ConfigureServices((h,s) =>
                //    {
                        
                //        //s.Add(new ServiceDescriptor(typeof(IWebApiCfg), cfg));
                     
                        
                //    .ConfigureWebHostDefaults(webBuilder =>
                //    {
                //        webBuilder.UseUrls("http://+:888/".Split(','));
                            
                //        //{

                //        //    if (env.IsDevelopment())
                //        //    {
                //        //        webBuilder.UseDeveloperExceptionPage();
                //        //    }
                //        //    else
                //        //    {
                //        //        server.UseHsts();
                //        //    }

                //        //    // server.UseHttpsRedirection();
                //        //    server.UseRouting();

                //        //    //server.UseAuthentication();
                //        //    //server.UseAuthorization();

                //        //    server.UseEndpoints(endpoints =>
                //        //    {
                //        //        endpoints.MapControllers();
                //        //    });


                //        //    //var webCfg = new HttpConfiguration();
                //        //    //if(_requestCfg != null)
                //        //    //{
                //        //    //    webCfg = _requestCfg(webCfg);
                //        //    //}
                //        //    //.RequireSsl()


                //        //    //webCfg = webCfg.UseAttributeRouting()
                //        //    //    .ResolveControllersWith(container)
                //        //    //    .LogErrorsWith(container);

                //        //    server.UseStaticFiles();
                //        //    var rxnsPortalRoot = new PhysicalFileProvider();
                //        //    var rxnsPortal = new FileServerOptions
                //        //    {
                //        //        EnableDefaultFiles = true,
                //        //        EnableDirectoryBrowsing = false,
                //        //        FileProvider = rxnsPortalRoot,
                //        //        StaticFileOptions = { FileProvider = rxnsPortalRoot, ServeUnknownFileTypes = true },
                //        //        DefaultFilesOptions = { DefaultFileNames = new[] { "index.html", } }
                //        //    };



                //        //    //  .AllowCrossDomain()
                //        //    // .Use<TokenInQueryStringToAuthorizationHeaderMiddleware>()


                //        //    //if (allowErrors)
                //        //    //{
                //        //    //    webCfg.PassthroughErrors();
                //        //    //    hubConfig.PassthroughErrors();
                //        //    //}

                //        //    //webCfg.EnableCompression(); //handle gzip streams
                //        //    //via middleware

                //        //    //the order here is important, you must set it before using the webapi
                //        //    //otherwise the controllers wont recognise the tokens and [Authorize] will fail
                //        //    server
                //        //        .AllowCrossDomain()
                //        //        //.WithAuthentication(authProvider, refreshProvider, encryptionKey)

                //        //        .UseFileServer(rxnsPortal);

                //        //    //.MapSignalRWithCrossDomain(hubConfig, authProvider, refreshProvider, encryptionKey);
                //        //})//UseStartup<ConfigureAndStartAspnetCore>()
                //        //   ;

                        
                        
                        
                //    })
                //    .Build();

                //    configuredWebApi.StartAsync();
                    stopServer = () => host.Dispose();

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

        private void CreateWebApi(IWebApiCfg cfg,/* IOAuthAuthorizationServerProvider authProvider, IAuthenticationTokenProvider refreshProvider, */ IApplicationBuilder server, IWebHostEnvironment env)
        {

         

        }
    }

}
