using System;
using System.Reactive.Disposables;
using System.Web.Http;
using Microsoft.AspNet.SignalR;
using Microsoft.Owin.FileSystems;
using Microsoft.Owin.Hosting;
using Microsoft.Owin.Security.Infrastructure;
using Microsoft.Owin.Security.OAuth;
using Microsoft.Owin.StaticFiles;
using Owin;
using Rxns.Hosting;
using Rxns.Interfaces;
using Rxns.WebApi.System.Web.Http;

namespace Rxns.WebApi.MsWebApiAdapters
{
    public interface IWebApiAdapter
    {
        /// <summary>
        /// todo: abstract out authstuff etc to make generic webservice component thats implemented by webapi2
        /// </summary>
        /// <param name="cfg"></param>
        /// <param name="container"></param>
        /// <param name="authProvider"></param>
        /// <param name="refreshProvider"></param>
        /// <param name="enableErrorMessages"></param>
        /// <param name="encryptionKey"></param>
        /// <param name="configurationAction"></param>
        /// <param name="reporter"></param>
        /// <returns></returns>
        IDisposable StartWebServices(IWebApiCfg cfg, IResolveTypes container, IOAuthAuthorizationServerProvider authProvider, IAuthenticationTokenProvider refreshProvider, bool enableErrorMessages = false, string encryptionKey = "notProtectedUntilYouSpecifcyOne", Action<IAppBuilder> configurationAction = null, IReportStatus reporter = null);
    }

    public class OwinWebApi2Adapter : IWebApiAdapter
    {
        private readonly Action<IAppBuilder> _hostCfg;
        private readonly Func<HttpConfiguration, HttpConfiguration> _requestCfg;

        public OwinWebApi2Adapter(Func<HttpConfiguration, HttpConfiguration> requestCfg = null, Action<IAppBuilder> hostCfg = null)
        {
            _requestCfg = requestCfg;
            _hostCfg = hostCfg;
        }

        public IDisposable StartWebServices(IWebApiCfg cfg, IResolveTypes container, IOAuthAuthorizationServerProvider authProvider, IAuthenticationTokenProvider refreshProvider, bool enableErrorMessages = false, string encryptionKey = "notProtectedUntilYouSpecifcyOne", Action<IAppBuilder> hostCfg = null, IReportStatus reporter = null)
        {
            try
            {
                var options = new StartOptions();
                foreach (var url in cfg.BindingUrl.Split(','))
                    options.Urls.Add(url);

                reporter?.OnInformation("Webservices listening on: {0}", cfg.BindingUrl);
                var stopService = WebApp.Start(options, builder =>
                {
                    _hostCfg?.Invoke(builder);
                    hostCfg?.Invoke(builder);

                    CreateWebApi(cfg, encryptionKey, authProvider, refreshProvider, builder, container, enableErrorMessages);
                });

                reporter?.OnInformation("Real-time hubs started");
                reporter?.OnInformation("Webservices listening on: {0}", cfg.BindingUrl);

                return new CompositeDisposable(stopService, new DisposableAction(() => reporter?.OnWarning("Shutting down host")));
            }
            catch (Exception e)
            {
                reporter?.OnError(e);
                return null;
            }
        }

        private void CreateWebApi(IWebApiCfg cfg, string encryptionKey, IOAuthAuthorizationServerProvider authProvider, IAuthenticationTokenProvider refreshProvider, IAppBuilder server, IResolveTypes container, bool allowErrors = false)
        {
            var webCfg = new HttpConfiguration();
            if(_requestCfg != null)
            {
                webCfg = _requestCfg(webCfg);
            }
            //.RequireSsl()
            webCfg = webCfg.UseAttributeRouting()
                .ResolveControllersWith(container)
                .LogErrorsWith(container);
            
            var rxnsPortalRoot = new PhysicalFileSystem(cfg.Html5Root);
            var rxnsPortal = new FileServerOptions
            {
                EnableDefaultFiles = true,
                FileSystem = rxnsPortalRoot,
                StaticFileOptions = { FileSystem = rxnsPortalRoot, ServeUnknownFileTypes = true },
                DefaultFilesOptions = { DefaultFileNames = new[] { cfg.Html5IndexHtml } }
            };


            var hubConfig = new HubConfiguration()
            {
                EnableJSONP = true,
                EnableDetailedErrors = allowErrors
            }
            .DisableProxies()
            .ResolveHubsWith(container);

            if (allowErrors)
            {
                webCfg.PassthroughErrors();
                hubConfig.PassthroughErrors();
            }
            
            webCfg.EnableCompression(); //handle gzip streams

            //the order here is important, you must set it before using the webapi
            //otherwise the controllers wont recognise the tokens and [Authorize] will fail
            server
                .AllowCrossDomain()
                .WithAuthentication(authProvider, refreshProvider, encryptionKey)
                .UseWebApi(webCfg)
                .UseFileServer(rxnsPortal)
                
                .MapSignalRWithCrossDomain(hubConfig, authProvider, refreshProvider, encryptionKey);

        }
    }

}
