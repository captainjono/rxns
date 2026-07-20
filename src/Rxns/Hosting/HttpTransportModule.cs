using System;
using System.Net.Http;
using System.Net.Http.Headers;
using Rxns.DDD.Commanding;
using Rxns.DDD.CQRS;
using Rxns.Health;
using Rxns.Hosting.Compression;
using Rxns.Hosting.Updates;
using Rxns.WebApi.Compression;

namespace Rxns.Hosting
{
    /// <summary>
    /// HTTP transport for cross-process events. Registers
    /// <see cref="HttpAppStatusServiceClient"/>, <see cref="HttpEventsServiceClient"/>,
    /// and <see cref="HttpUpdateServiceClient"/> as the bindings for
    /// <c>IAppStatusServiceClient</c>, <c>IEventsServiceClient</c>,
    /// <c>IUpdateServiceClient</c>.
    ///
    /// <para>
    /// Use as a sibling to <see cref="AppStatusCoreModule"/> when the host
    /// posts events to a remote arena over HTTP (the original Rxns default).
    /// Mutually exclusive with <c>RedisTransportModule</c>; the composition
    /// root picks one based on configuration so there's no last-registration
    /// fight.
    /// </para>
    /// </summary>
    public class HttpTransportModule : IAppModule
    {
        public IRxnLifecycle Load(IRxnLifecycle lifecycle)
        {
            return lifecycle
                .CreatesOncePerApp(c =>
                {
                    var cfg = c.Resolve<HttpClientCfg>();
                    var client = new HttpClient(new TransferCompressionHandler(c.Resolve<IRxnHealthManager>(), cfg, new GZipCompressor()));
                    client.DefaultRequestHeaders.Accept.Clear();
                    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    client.Timeout = cfg.TotalTransferTimeout;
                    return client;
                })
                .CreatesOncePerAppNamed<AnonymousHttpConnection, IHttpConnection>("anonymous")
                .CreatesOncePerAppNamed<AuthenticatedHttpConnection, IHttpConnection>("authenticated")
                .CreatesOncePerApp(c =>
                {
                    var cfg = c.Resolve<IAppServiceRegistry>();
                    return new CommandServiceClientCfg() { BaseUrl = cfg.AppStatusUrl };
                }, true)
                .CreatesOncePerApp(cc =>
                {
                    var anonConnection = cc.ResolveTag<IHttpConnection>("anonymous");
                    var authedConnection = cc.ResolveTag<IHttpConnection>("authenticated");
                    var credentials = cc.Resolve<ITenantCredentials>();
                    var cfg = cc.Resolve<IAppServiceRegistry>();
                    return new CommandServiceClient(cfg, authedConnection, anonConnection, credentials);
                }, true)
                .CreatesOncePerApp<HttpEventsServiceClient>()
                .CreatesOncePerApp<HttpAppStatusServiceClient>()
                .CreatesOncePerApp<HttpUpdateServiceClient>()
                .CreatesOncePerApp<AppUpdateServiceClient>()
                .CreatesOncePerApp(() => new HttpClientCfg()
                {
                    TotalTransferTimeout = TimeSpan.FromMinutes(5),
                    EnableCompression = true
                });
        }
    }
}
