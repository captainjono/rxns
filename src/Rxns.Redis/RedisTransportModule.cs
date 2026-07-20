using System;
using System.Collections.Generic;
using Rxns.Cloud;
using Rxns.Health;
using Rxns.Hosting;
using Rxns.Hosting.Updates;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns.Redis
{
    /// <summary>
    /// Redis Streams transport for cross-process events. Sibling to
    /// <see cref="Rxns.Hosting.HttpTransportModule"/> - mutually exclusive
    /// with it. Use this with <see cref="Rxns.Hosting.AppStatusCoreModule"/>
    /// to wire a host whose central transport is Redis Streams instead of
    /// HTTP / SignalR.
    ///
    /// <para>
    /// What gets registered (when applicable):
    /// </para>
    /// <list type="bullet">
    ///   <item><see cref="RedisAppStatusServiceClient"/> as <c>IAppStatusServiceClient</c>
    ///         - the canonical cross-process publish surface. Mode is
    ///         <c>Bidirectional</c> for arena hosts (consume + publish) and
    ///         <c>PublishOnly</c> for worker hosts (publish only; inbound
    ///         routed cmds ride a separate stream via
    ///         <see cref="RedisRoutedCmdConsumer"/>).</item>
    ///   <item><see cref="RedisEventHub"/> as <c>IEventHub</c> /
    ///         <c>IAppCmdManager</c> on arena hosts - cluster-internal
    ///         command routing publishes to the host-supplied
    ///         routed-cmds stream.</item>
    ///   <item><see cref="RedisRoutedCmdConsumer"/> on worker hosts -
    ///         filters routed cmds by clientId and dispatches to local bus.</item>
    ///   <item><see cref="RedisInboundEventPump"/> on hosts with a
    ///         Bidirectional client - subscribes
    ///         <see cref="RedisAppStatusServiceClient.Incoming"/> and pumps
    ///         events onto the local <c>IRxnManager</c> so type-based
    ///         subscriptions fire for events from remote processes.</item>
    /// </list>
    ///
    /// <para>
    /// The <see cref="HostKind"/> selector tells the module whether to wire
    /// the arena-shape (Bidirectional client + EventHub + pump) or the
    /// worker-shape (PublishOnly client + RoutedCmdConsumer). One module
    /// type, one config knob - no last-wins ordering hacks because the
    /// composition root just doesn't include the HTTP module when Redis is
    /// active.
    /// </para>
    /// </summary>
    public class RedisTransportModule : IAppModule
    {
        public enum HostKind
        {
            Arena,
            Worker
        }

        public string RedisConnectionString { get; }
        public string EventStream { get; }
        public string ConsumerGroup { get; }
        public string RoutedCmdsStream { get; }
        public string ArenaCmdsConsumerGroup { get; }
        public string WorkerCmdsConsumerGroupPrefix { get; }
        public HostKind Kind { get; }

        public RedisTransportModule(
            string redisConnectionString,
            HostKind kind,
            string eventStream,
            string arenaConsumerGroup,
            string workerConsumerGroup,
            string routedCmdsStream,
            string arenaCmdsConsumerGroup,
            string workerCmdsConsumerGroupPrefix)
        {
            RedisConnectionString = redisConnectionString;
            Kind = kind;
            EventStream = eventStream;
            ConsumerGroup = kind == HostKind.Arena ? arenaConsumerGroup : workerConsumerGroup;
            RoutedCmdsStream = routedCmdsStream;
            ArenaCmdsConsumerGroup = arenaCmdsConsumerGroup;
            WorkerCmdsConsumerGroupPrefix = workerCmdsConsumerGroupPrefix;
        }

        public IRxnLifecycle Load(IRxnLifecycle lifecycle)
        {
            $"RedisTransportModule: kind={Kind} stream='{EventStream}' group='{ConsumerGroup}'".LogDebug();

            // IAppStatusServiceClient: Bidirectional for arena, PublishOnly
            // for worker. Eager-start of the inbound poll happens in the
            // channel's ctor for any subscribe-capable mode (closes the
            // publish-before-listening race).
            lifecycle.CreatesOncePerApp<IAppStatusServiceClient>(cc =>
            {
                var mode = Kind == HostKind.Arena
                    ? RedisStreamMode.Bidirectional
                    : RedisStreamMode.PublishOnly;

                // Bind an HTTP fallback for binary log uploads. Redis Streams
                // carry structured events; per-test log zips ride the arena's
                // /systemstatus/logs/.../publish multipart endpoint, which
                // works identically under both transports. Without this,
                // ShipLogForTest silently drops every worker's tape archive
                // (phase 7m: report read 0 clients despite real execution).
                IAppStatusServiceClient httpFallback = null;
                try { httpFallback = new HttpAppStatusServiceClient(
                        cc.Resolve<IHttpConnection>(),
                        cc.Resolve<ICreateEvents>(),
                        cc.Resolve<IRxnAppInfo>(),
                        cc.Resolve<ITenantCredentials>(),
                        cc.Resolve<IAppServiceRegistry>()); }
                catch (Exception ex)
                {
                    $"RedisTransportModule: HTTP fallback unavailable ({ex.GetType().Name}: {ex.Message}) — PublishLog will no-op".LogDebug();
                }

                return new RedisAppStatusServiceClient(
                    RedisConnectionString,
                    EventStream,
                    ConsumerGroup,
                    mode: mode,
                    httpFallback: httpFallback);
            });

            if (Kind == HostKind.Arena)
            {
                // Cluster-internal command routing.
                lifecycle.CreatesOncePerApp<RedisEventHub>(cc =>
                    new RedisEventHub(
                        RedisConnectionString,
                        cc.Resolve<IAppStatusStore>(),
                        cc.Resolve<Rxns.Hosting.Updates.IAppCmdManager>(),
                        cc.Resolve<IRxnManager<IRxn>>(),
                        cc.Resolve<IResolveTypes>(),
                        RoutedCmdsStream,
                        ArenaCmdsConsumerGroup));

                // Pump inbound stream events onto the local IRxnManager so
                // RedisEventHub's type subscriptions fire for events sent
                // by remote workers.
                lifecycle.CreatesOncePerApp<RedisInboundEventPump>();
            }
            else
            {
                // Worker reads routed commands from the dedicated
                // routed-cmds stream + emits heartbeats (which double as
                // route registration on the arena's RedisEventHub).
                lifecycle.CreatesOncePerApp<RedisRoutedCmdConsumer>(cc =>
                    new RedisRoutedCmdConsumer(
                        RedisConnectionString,
                        cc.Resolve<IRouteProvider>().GetLocalBaseRoute(),
                        cc.Resolve<IRxnManager<IRxn>>(),
                        cc.Resolve<IAppStatusServiceClient>(),
                        cc.Resolve<IResolveTypes>(),
                        RoutedCmdsStream,
                        WorkerCmdsConsumerGroupPrefix));
            }

            return lifecycle;
        }
    }
}
