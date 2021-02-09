using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Rxns.Hosting.Compression;
using Rxns.Interfaces;
using Rxns.WebApi.Compression;
using Rxns.WebApiNET5.NET5WebApiAdapters.RxnsApiAdapters;

namespace Rxns.WebApiNET5.NET5WebApiAdapters
{
    namespace System.Web.Http
    {
        /// <summary>
        /// Extension methods to configure WebApi services in a fluent manner
        /// </summary>
        public static class WebApiExtensions
        {
     

            ///// <summary>
            ///// Allows other computer based clients to communicate with the service from other
            ///// domains
            ///// </summary>
            ///// <param name="config"></param>
            ///// <returns></returns>
            //public static HttpConfiguration EnableCorForAll(this HttpConfiguration config)
            //{
            //    var cors = new EnableCorsAttribute("*", "*", "*");
            //    config.EnableCors(cors);

            //    return config;
            //}

            /// <summary>
            /// Allows other computer based clients to communicate with the service from any domain
            /// </summary>
            /// <param name="builder"></param>
            /// <returns></returns>
            public static IApplicationBuilder AllowCrossDomain(this IApplicationBuilder builder)
            {
                builder.UseCors(p =>
                {
                    p.AllowAnyHeader()
                        .AllowAnyMethod()
                        .AllowAnyOrigin();
                    //.AllowCredentials();
                });

                return builder;
            }


            ///// <summary>
            ///// Configures an app to use the following provider for authentication
            ///// challenges when using the [Authorize] attribute in controllers
            ///// 
            ///// Then authentication scheme is OAuth2 bearer tokens
            ///// 
            ///// NOTE: MUST request tokens via SSL otherwise this authentications scheme is useless
            ///// </summary>
            ///// <param name="builder"></param>
            ///// <param name="provider">The authentication provider</param>
            ///// <param name="refreshProvider">The refesh token provider. NOTE: Not used at the moment</param>
            ///// <param name="encryptionKey">The key used to encrypt the token provided when logging in</param>
            ///// <returns></returns>
            //public static IApplicationBuilder WithAuthentication(this IApplicationBuilder builder, IOAuthAuthorizationServerProvider provider, IAuthenticationTokenProvider refreshProvider, string encryptionKey)
            //{
            //    builder.UseOAuthAuthorizationServer(new OAuthAuthorizationServerOptions()
            //    {
            //        AllowInsecureHttp = true,
            //        TokenEndpointPath = new PathString("/token"),
            //        AccessTokenExpireTimeSpan = TimeSpan.FromDays(1),
            //        Provider = provider,
            //        AccessTokenFormat = new SecureTokenFormatter(encryptionKey)
            //        //not used at the moment
            //        //
            //        //RefreshTokenProvider = refreshProvider
            //    });

            //    builder.UseOAuthBearerAuthentication(new OAuthBearerAuthenticationOptions()
            //    {
            //        AuthenticationType = "Bearer",
            //        AuthenticationMode = AuthenticationMode.Active,
            //        Provider = new DynamicOAuthTokenProvider(req => req.Query.Get("bearer_token")),
            //        AccessTokenFormat = new SecureTokenFormatter(encryptionKey)
            //    });

            //    return builder;
            //}

            //public static HubConfiguration PassthroughErrors(this HubConfiguration config)
            //{
            //    config.EnableDetailedErrors = true;

            //    return config;
            //}

            //public static HubConfiguration DisableProxies(this HubConfiguration config)
            //{
            //    config.EnableJavaScriptProxies = false;

            //    return config;
            //}

            //public static HubConfiguration ResolveHubsWith(this HubConfiguration config, IResolveTypes container)
            //{
            //    //GlobalHost.DependencyResolver.Register(typeof(JsonSerializer), () => serializer);
            //    config.Resolver = new RxnsDependencyResolver(container);

            //    return config;
            //}

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

                    var headerParameters = new RouteValueDictionary(formValues);
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
