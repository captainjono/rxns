using System;
using System.Linq;
using Autofac;
using Autofac.Features.OwnedInstances;
using Microsoft.AspNet.SignalR.Client;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Rxns.DDD;
using Rxns.Health.AppStatus;
using Rxns.Hosting;
using Rxns.Logging;
using Rxns.WebApiNET5.NET5WebApiAdapters.RxnsApiAdapters;

namespace Rxns.WebApiNET5.NET5WebApiAdapters
{
    public class AspNetCoreWebApiAdapterModule : IAppModule
    {
        public IRxnLifecycle Load(IRxnLifecycle lifecycle)
        {

            return lifecycle
                .CreatesOncePerApp<ReportHub>()
                .CreatesOncePerApp<SystemMetricsHub>()
                // EventsHub implements IEventHub (the transport-neutral cluster
                // event-bus surface). AsImplementedInterfaces picks up IEventHub +
                // IAppCmdManager + IRxnLogger so existing consumers binding to those
                // interfaces resolve to this same singleton. A non-HTTP transport
                // (e.g. Redis Streams) can override IEventHub via last-registration-
                // wins from a sibling transport module.
                .CreatesOncePerApp<EventsHub>()
                .CreatesOncePerRequest<EventController>()
                .CreatesOncePerApp<RemoteReportStatusEcho>()
                .CreatesOncePerApp<SystemMetricsReport>()
                .CreatesOncePerApp<AspNetCoreWebApiAdapter>()
                .CreatesOncePerApp<AspnetCoreOnReadyHandler>()

                //.CreatesOncePerApp<HostBuffer>()


                //.CreatesOncePerAppAs<NoOAuthAuthentication, OAuthAuthorizationServerProvider>()

                .CreatesOncePerRequest<SystemStatusController>()
                .CreatesOncePerRequest<ErrorsController>()
                .CreatesOncePerRequest<UpdatesController>()
                .CreatesOncePerApp<MultipartFormDataUploadProvider>()
                .CreatesOncePerApp<StaticFileSystemConfiguration>()
                .CreatesOncePerRequest<CommandWebApiController>()
                .CreatesOncePerRequest<AnonymousCommandController>()
                //.CreatesOncePerApp<AspnetCoreControllerLinkProvider>()
                .CreatesOncePerApp<ResolverCommandFactory>()
                .Includes<AppStatusServerModule>() //server modules always after client module
                .Includes<DDDServerModule>()

                //this is a connection factory takes a url and returns a signalR client
                .CreatesOncePerApp<Func<string, Owned<HubConnection>>>(c =>
                {
                    var cc = c.Resolve<IComponentContext>();
                    return (url) =>
                    {
                        var lifetime = cc.Resolve<ILifetimeScope>().BeginLifetimeScope();
                        // Bump client-side receive cap to 4 MB to match the server's
                        // AddHubOptions<EventsHub> setting. SignalR's CLIENT default is
                        // also 32 KB — separate from the SERVER-side AddHubOptions cap
                        // — and trips on incoming server-pushed events with a
                        // misleading "server closed the connection: maximum message
                        // size of 32768B was exceeded" wording. Both ends must match.
                        var connection = BuildHubConnection(url);
                        return new Owned<HubConnection>(connection, lifetime);
                    };
                })
                ;

            //so adapters can be swapped out
            ;


        }

        /// <summary>
        /// Builds the hub connection every client in the process uses, timeouts and all.
        ///
        /// <para>Public and named so a test can assert against the connection production actually
        /// gets. Asserting against a copy of this configuration would have passed just as happily
        /// while the real one carried SignalR's 30 second default - which is the timeout that
        /// stopped a hundred-device rung at 35 results.</para>
        /// </summary>
        public static HubConnection BuildHubConnection(string url)
        {
            // Bump client-side receive cap to 4 MB to match the server's
            // AddHubOptions<EventsHub> setting. SignalR's CLIENT default is
            // also 32 KB - separate from the SERVER-side AddHubOptions cap -
            // and trips on incoming server-pushed events with a misleading
            // "server closed the connection: maximum message size of 32768B
            // was exceeded" wording. Both ends must match.
            var builder = new HubConnectionBuilder().WithUrl(url);
            builder.Services.Configure<Microsoft.AspNetCore.SignalR.HubOptions>(opts =>
            {
                opts.MaximumReceiveMessageSize = 4L * 1024 * 1024;
            });

            var connection = builder
                .WithAutomaticReconnect(new AlwaysRetryPolicy(() => TimeSpan.FromSeconds(1)))
                .Build();

            // The client gives up on a silent server after ServerTimeout, and its default is 30
            // SECONDS. That is the timeout that ended a 100-device rung: the arena, busy serving
            // the fleet, took 33.3s to answer one request, so the leg carrying every dispatch and
            // every result was torn down - nine consecutive "Server timeout (30000.00ms) elapsed"
            // before it reconnected into a run that never resumed. Results stopped at 35 of 100.
            //
            // 120s is three and a half times that worst case, and holds the rule the pair has to
            // satisfy: ServerTimeout >= 2 x KeepAliveInterval, which is 45s. The old pairing was
            // 30s against a 20s ping - 1.5x - so the margin was already too thin before the arena
            // was under any load.
            //
            // A handshake has to survive the same congestion. 15s is the default, and a reconnect
            // arriving mid-fan-out has no better chance of being answered quickly than the request
            // that timed out in the first place.
            connection.ServerTimeout = TimeSpan.FromSeconds(120);
            connection.HandshakeTimeout = TimeSpan.FromSeconds(30);
            connection.KeepAliveInterval = TimeSpan.FromSeconds(45);

            return connection;
        }

    }
    
    /// <summary>
    /// Exponential backoff retry policy capped at 30s. The constructor's base
    /// delay is the FIRST retry delay; subsequent retries scale 1x, 2x, 5x,
    /// 10x, 30x of the base, capped at 30s after that. This shape:
    ///   - Recovers a genuine transient blip on the next retry — no throughput
    ///     penalty for normal ops where 1-2 retries clear the problem.
    ///   - Backs off hard when something is actually broken — by retry #4 the
    ///     client is waiting 10s+ between attempts, so a storming endpoint
    ///     that keeps rejecting frames can't sustain a 1Hz reconnect cycle.
    ///   - Always retries forever (returns non-null) — never gives up.
    ///
    /// The retry count resets on any successful reconnect.
    /// </summary>
    public class AlwaysRetryPolicy : IRetryPolicy
    {
        private readonly Func<TimeSpan> _baseDelay;
        public const int MaxBackoffSeconds = 30;

        public AlwaysRetryPolicy(Func<TimeSpan> baseDelay)
        {
            _baseDelay = baseDelay;
        }

        public TimeSpan? NextRetryDelay(RetryContext retryContext)
        {
            var baseSecs = _baseDelay().TotalSeconds;
            // Multiplier ramp keyed off PreviousRetryCount. SignalR resets this
            // to 0 on every successful reconnect, so a transient stays transient.
            var mult = retryContext.PreviousRetryCount switch
            {
                0 => 1.0,   // first retry — fast
                1 => 2.0,   // 2nd retry — still optimistic
                2 => 5.0,   // 3rd — slowing down
                3 => 10.0,  // 4th — backing off
                _ => 30.0,  // 5th+ — hard cap territory
            };
            var secs = Math.Min(baseSecs * mult, MaxBackoffSeconds);
            return TimeSpan.FromSeconds(secs);
        }
    }
}