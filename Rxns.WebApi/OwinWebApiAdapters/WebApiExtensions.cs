using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Formatting;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Web.Cors;
using System.Web.Http;
using System.Web.Http.Cors;
using System.Web.Http.ExceptionHandling;
using System.Web.Http.Routing;
using Microsoft.AspNet.SignalR;
using Microsoft.Owin;
using Microsoft.Owin.Cors;
using Microsoft.Owin.Security;
using Microsoft.Owin.Security.Infrastructure;
using Microsoft.Owin.Security.OAuth;
using Owin;
using Rxns.Hosting.Compression;
using Rxns.Interfaces;
using Rxns.Microservices;
using Rxns.WebApi.Compression;
using Rxns.WebApi.MsWebApiAdapters;
using Rxns.WebApi.OwinWebApiAdapters;

namespace Rxns.WebApi
{
    namespace System.Web.Http
    {
        /// <summary>
        /// Extension methods to configure WebApi services in a fluent manner
        /// </summary>
        public static class WebApiExtensions
        {
            /// <summary>
            /// Tells the service to resolve all controllers for each request through
            /// the specified container
            /// </summary>
            /// <param name="config"></param>
            /// <param name="container">The container to use for class instanciation</param>
            /// <returns></returns>
            public static HttpConfiguration LogErrorsWith(this HttpConfiguration config, IResolveTypes container)
            {
                config.Services.Add(typeof(IExceptionLogger), container.Resolve<IExceptionLogger>());
                return config;
            }

            /// <summary>
            /// Tells the service to resolve all controllers for each request through
            /// the specified container
            /// </summary>
            /// <param name="config"></param>
            /// <param name="container">The container to use for class instanciation</param>
            /// <returns></returns>
            public static HttpConfiguration ResolveControllersWith(this HttpConfiguration config, IResolveTypes container)
            {
                config.DependencyResolver = new RxnxDependencyResolver(container);
                return config;
            }


            /// <summary>
            /// Enables the use of [Route] attributes in controllers instead of using conventions
            /// </summary>
            /// <param name="config"></param>
            /// <returns></returns>
            public static HttpConfiguration UseAttributeRouting(this HttpConfiguration config)
            {
                //enable the [Routes] attribute on controllers
                config.MapHttpAttributeRoutes();

                return config;
            }

            /// <summary>
            /// Enables errors to be displayed to the users instead of generic error messages
            /// </summary>
            /// <param name="config"></param>
            /// <returns></returns>
            public static HttpConfiguration PassthroughErrors(this HttpConfiguration config)
            {
                config.IncludeErrorDetailPolicy = IncludeErrorDetailPolicy.Always;

                return config;
            }

            public static void AddXmlFortmattingQueryStringOption(this HttpConfiguration config)
            {
                //add option to format as xml
                config.Formatters.Add(new XmlMediaTypeFormatter());
                config.Formatters.XmlFormatter.MediaTypeMappings.Add(new QueryStringMapping("$format", "xml", "application/xml"));
                config.Formatters.JsonFormatter.SupportedMediaTypes.Add(new MediaTypeHeaderValue("text/html"));
            }

            public static HttpConfiguration RequireSsl(this HttpConfiguration config)
            {
                //remove XML WS
                config.Filters.Add(new RequireSslFilter());
                config.MessageHandlers.Add(new RequireHttpHeaderForSsl());

                return config;
            }

            public static HttpConfiguration EnableCompression(this HttpConfiguration config)
            {
                config.MessageHandlers.Insert(0, new CompressedRequestOrResponseHandler(new ICompressionHandler[] { new CompressionHandler(new GZipCompressor()) }));

                return config;
            }

            /// <summary>
            /// Allows other computer based clients to communicate with the service from other
            /// domains
            /// </summary>
            /// <param name="config"></param>
            /// <returns></returns>
            public static HttpConfiguration EnableCorForAll(this HttpConfiguration config)
            {
                var cors = new EnableCorsAttribute("*", "*", "*");
                config.EnableCors(cors);

                return config;
            }

            /// <summary>
            /// Allows other computer based clients to communicate with the service from any domain
            /// </summary>
            /// <param name="builder"></param>
            /// <returns></returns>
            public static IAppBuilder AllowCrossDomain(this IAppBuilder builder)
            {
                builder.UseCors(new CorsOptions()
                {
                    PolicyProvider = new CorsPolicyProvider()
                    {
                        PolicyResolver = context => Task.FromResult(new CorsPolicy()
                        {
                            AllowAnyHeader = true,
                            AllowAnyMethod = true,
                            AllowAnyOrigin = true,
                            SupportsCredentials = true
                        })
                    }
                });

                return builder;
            }

            /// <summary>
            /// Allows other computer based clients to communicate with the service from any domain
            /// </summary>
            /// <param name="builder"></param>
            /// <param name="configuration"></param>
            /// <param name="encryptionKey"></param>
            /// <param name="hubName"></param>
            /// <param name="encryptionKey">The key used to encrypt the token provided when logging in</param>
            /// <returns></returns>
            public static IAppBuilder MapSignalRWithCrossDomain(this IAppBuilder builder, HubConfiguration configuration, IOAuthAuthorizationServerProvider provider, IAuthenticationTokenProvider refreshProvider, string encryptionKey, string hubName = "/signalr")
            {
                builder.Map(hubName, app =>
                {
                    app.WithAuthentication(provider, refreshProvider, encryptionKey)
                       .AllowCrossDomain()
                       .Use<TokenInQueryStringToAuthorizationHeaderMiddleware>()
                       .RunSignalR(configuration);
                });

                //builder.Map("/signalr", map =>
                //{
                //    map.AllowCrossDomain()
                //       .RunSignalR(configuration);
                //});

                return builder;
            }

            /// <summary>
            /// Configures an app to use the following provider for authentication
            /// challenges when using the [Authorize] attribute in controllers
            /// 
            /// Then authentication scheme is OAuth2 bearer tokens
            /// 
            /// NOTE: MUST request tokens via SSL otherwise this authentications scheme is useless
            /// </summary>
            /// <param name="builder"></param>
            /// <param name="provider">The authentication provider</param>
            /// <param name="refreshProvider">The refesh token provider. NOTE: Not used at the moment</param>
            /// <param name="encryptionKey">The key used to encrypt the token provided when logging in</param>
            /// <returns></returns>
            public static IAppBuilder WithAuthentication(this IAppBuilder builder, IOAuthAuthorizationServerProvider provider, IAuthenticationTokenProvider refreshProvider, string encryptionKey)
            {
                builder.UseOAuthAuthorizationServer(new OAuthAuthorizationServerOptions()
                {
                    AllowInsecureHttp = true,
                    TokenEndpointPath = new PathString("/token"),
                    AccessTokenExpireTimeSpan = TimeSpan.FromDays(1),
                    Provider = provider,
                    AccessTokenFormat = new SecureTokenFormatter(encryptionKey)
                    //not used at the moment
                    //
                    //RefreshTokenProvider = refreshProvider
                });

                builder.UseOAuthBearerAuthentication(new OAuthBearerAuthenticationOptions()
                {
                    AuthenticationType = "Bearer",
                    AuthenticationMode = AuthenticationMode.Active,
                    Provider = new DynamicOAuthTokenProvider(req => req.Query.Get("bearer_token")),
                    AccessTokenFormat = new SecureTokenFormatter(encryptionKey)
                });

                return builder;
            }

            public static HubConfiguration PassthroughErrors(this HubConfiguration config)
            {
                config.EnableDetailedErrors = true;

                return config;
            }

            public static HubConfiguration DisableProxies(this HubConfiguration config)
            {
                config.EnableJavaScriptProxies = false;

                return config;
            }

            public static HubConfiguration ResolveHubsWith(this HubConfiguration config, IResolveTypes container)
            {
                //GlobalHost.DependencyResolver.Register(typeof(JsonSerializer), () => serializer);
                config.Resolver = new RxnsDependencyResolver(container);

                return config;
            }

            //    public class SignalRContractResolver : IContractResolver
            //    {
            //        private readonly Assembly _assembly;
            //        private readonly IContractResolver _camelCaseContractResolver;
            //        private readonly IContractResolver _defaultContractSerializer;

            //        public SignalRContractResolver()
            //        {
            //            _defaultContractSerializer = new DefaultContractResolver();
            //            _camelCaseContractResolver = new CamelCasePropertyNamesContractResolver();
            //            _assembly = typeof(Connection).Assembly;
            //        }

            //        public JsonContract ResolveContract(Type type)
            //        {
            //            if (type.Assembly.Equals(_assembly))
            //            {
            //                return _defaultContractSerializer.ResolveContract(type);
            //            }

            //            return _camelCaseContractResolver.ResolveContract(type);
            //        }
            //    }
            //}
        }

        public class RxnsDependencyResolver: DefaultDependencyResolver
        {
            private readonly IResolveTypes _scope;
            
            public RxnsDependencyResolver(IResolveTypes container)
            {
                if (container == null)
                    throw new ArgumentNullException(nameof(IAppContainer));

                this._scope = container.BegingScope();
            }

            /// <summary>
            /// Gets the Autofac implementation of the dependency resolver.
            /// </summary>
            public static RxnsDependencyResolver Current
            {
                get
                {
                    return GlobalHost.DependencyResolver as RxnsDependencyResolver;
                }
            }

            /// <summary>Get a single instance of a service.</summary>
            /// <param name="serviceType">Type of the service.</param>
            /// <returns>The single instance if resolved; otherwise, <c>null</c>.</returns>
            public override object GetService(Type serviceType)
            {
                return _scope.Resolve(serviceType) ?? base.GetService(serviceType);
            }

            /// <summary>Gets all available instances of a services.</summary>
            /// <param name="serviceType">Type of the service.</param>
            /// <returns>The list of instances if any were resolved; otherwise, an empty list.</returns>
            public override IEnumerable<object> GetServices(Type serviceType)
            {
                IEnumerable<object> source = (IEnumerable<object>)_scope.Resolve(typeof(IEnumerable<>).MakeGenericType(serviceType));
                return !source.Any<object>() ? base.GetServices(serviceType) : source;
            }
            
        }

        namespace System.Net.Http
        {
            public static class HttpExtensions
            {
                /// <summary>
                /// Extensions for <see cref="MultipartFormDataContent"/>.
                /// </summary>

                public static void Add(this MultipartFormDataContent form, HttpContent content, object formValues)
                {
                    Add(form, content, formValues);
                }

                /// <summary>
                /// used to add name-value-pair objects to a request, that get populated into the formdata value returned
                /// from the MultipartFormDataStreamProvider
                /// </summary>
                /// <param name="form"></param>
                /// <param name="content"></param>
                /// <param name="name">The name (FormData.Key)</param>
                /// <param name="formValues">The value (FormData.Value)</param>
                public static void Add(this MultipartFormDataContent form, HttpContent content, string name, object formValues)
                {
                    Add(form, content, formValues, name: name);
                }

                public static void Add(this MultipartFormDataContent form, HttpContent content, string name, string fileName, object formValues)
                {
                    Add(form, content, formValues, name: name, fileName: fileName);
                }

                public static void AddFormData(this MultipartFormDataContent form, string key, object value)
                {
                    form.Add(new StringContent(value.ToString()), key);
                }

                private static void Add(this MultipartFormDataContent form, HttpContent content, object formValues, string name = null, string fileName = null)
                {
                    var header = new ContentDispositionHeaderValue("form-data");
                    header.Name = name;
                    header.FileName = fileName;
                    header.FileNameStar = fileName;

                    var headerParameters = new HttpRouteValueDictionary(formValues);
                    foreach (var parameter in headerParameters)
                    {
                        header.Parameters.Add(new NameValueHeaderValue(parameter.Key, parameter.Value.ToString()));
                    }

                    content.Headers.ContentDisposition = header;
                    form.Add(content);
                }
            }
        }
    }
}
