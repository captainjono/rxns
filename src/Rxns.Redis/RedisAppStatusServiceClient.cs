using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using Rxns.Cloud;
using Rxns.Health;
using Rxns.Hosting.Updates;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns.Redis
{
    /// <summary>
    /// Redis Streams implementation of <see cref="IAppStatusServiceClient"/> —
    /// the canonical cross-process publish surface in rxns. Mirrors the role
    /// of <c>SignalRRxnManagerBridge</c> but ships every published event via
    /// a Redis Stream instead of a SignalR hub invocation.
    ///
    /// <para>
    /// Architecture goal: collapse the two pre-existing cross-process publish
    /// paths (typed-RxnManager → AppStatusBackingChannel → service-client; AND
    /// SystemStatusPublisher.Process → service-client.PublishSystemStatus)
    /// into ONE transport boundary. Whichever transport is registered as the
    /// service-client carries every cross-process event — no doubling, no
    /// per-mode special cases at the publisher level.
    /// </para>
    ///
    /// <para>
    /// Publish surface:
    ///   - <see cref="Publish(IEnumerable{IRxn})"/> → each event shipped via the
    ///     internal <see cref="RedisStreamBackingChannel{T}"/>.
    ///   - <see cref="PublishSystemStatus"/> → wrap as <see cref="AppHeartbeat"/>,
    ///     ship via the same channel. Returns empty <see cref="IRxnQuestion"/>[]
    ///     (Redis carries events, not the synchronous command-response that
    ///     SignalR's HTTP fallback used to piggyback).
    ///   - <see cref="PublishError"/> → ship as IRxn via the channel.
    ///   - <see cref="DeleteError"/>, <see cref="PublishLog"/> → no-op
    ///     (log shipping rides the host's own HTTP path, not the event bus).
    /// </para>
    ///
    /// <para>
    /// Inbound: the wrapped channel's <see cref="RedisStreamBackingChannel{T}.Setup"/>
    /// returns the incoming-events observable. Callers subscribe and pump into
    /// <c>RxnsLocal.Publish</c> so cross-process events surface on the local
    /// bus exactly like SignalR's server-push does. We expose
    /// <see cref="Incoming"/> for that wiring.
    /// </para>
    /// </summary>
    public class RedisAppStatusServiceClient : ReportsStatus, IAppStatusServiceClient, IDisposable
    {
        private readonly RedisStreamBackingChannel<IRxn> _channel;
        private readonly IAppStatusServiceClient _httpFallback;

        public RedisAppStatusServiceClient(
            string redisConnectionString,
            string streamName,
            string consumerGroup,
            RedisStreamMode mode = RedisStreamMode.Bidirectional,
            IAppStatusServiceClient httpFallback = null)
        {
            _httpFallback = httpFallback;
            // Mode controls whether the inbound poll loop starts:
            // - Arena: Bidirectional / Subscribe — needs to consume worker-published
            //   events (heartbeats, results) and re-emit them onto the local bus.
            // - Worker: PublishOnly — only writes events for the arena to read; doesn't
            //   need to receive other workers' events on the typed-events stream.
            // Eager-start in the channel constructor closes the publish-before-listening
            // race that loses concurrent-boot worker heartbeats.
            _channel = new RedisStreamBackingChannel<IRxn>(
                redisConnectionString,
                streamName,
                consumerGroup,
                mode: mode);

            OnInformation("RedisAppStatusServiceClient: stream='{0}' group='{1}' mode={2}", streamName, consumerGroup, mode);

            // Pre-warm Setup so the channel's ready-to-deliver observable is
            // bound before any caller accesses Incoming. Cheap; avoids a
            // late-Subscribe gap where ctor-published events are buffered in
            // _incoming but no subscriber's listening yet.
            if (mode != RedisStreamMode.PublishOnly)
                _ = _channel.Setup(new PassthroughDelivery<IRxn>());
        }

        /// <summary>
        /// Inbound observable of events received from the Redis stream. Wire
        /// into the local bus to surface cross-process events.
        /// </summary>
        public IObservable<IRxn> Incoming => _channel.Setup(new PassthroughDelivery<IRxn>());

        public IObservable<Unit> Publish(IEnumerable<IRxn> events)
        {
            return Rxn.Create(() =>
            {
                if (events == null) return;
                foreach (var e in events)
                {
                    if (e == null) continue;
                    _channel.Publish(e);
                }
            });
        }

        public IObservable<Unit> PublishError(BasicErrorReport report)
        {
            return Rxn.Create(() =>
            {
                if (report != null) _channel.Publish(report);
            });
        }

        public IObservable<Unit> DeleteError(long id) => Observable.Return(Unit.Default);

        public IObservable<IRxnQuestion[]> PublishSystemStatus(SystemStatusEvent status, AppStatusInfo[] meta)
        {
            return Rxn.Create<IRxnQuestion[]>(() =>
            {
                _channel.Publish(new AppHeartbeat(status, meta));
                return Array.Empty<IRxnQuestion>();
            });
        }

        public IObservable<string> PublishLog(Stream zippedLog)
        {
            // Binary log uploads always traverse HTTP. Redis Streams is for
            // structured events; pushing zipped tape archives through stream
            // entries is wasteful (each entry is a string-keyed map) and
            // we'd need base64 framing on top. The arena's
            // /systemstatus/logs/.../publish multipart endpoint works
            // identically under both transports — only the event bus changed.
            //
            // Without this delegation, ShipLogForTest's PublishLog returns
            // an empty string, the worker's per-test tape zip is dropped,
            // and the perf-test-results.html shows 0 clients despite real
            // worker execution — phase 7m symptom.
            if (_httpFallback != null)
                return _httpFallback.PublishLog(zippedLog);

            OnInformation("RedisAppStatusServiceClient.PublishLog: no HTTP fallback registered — log upload skipped");
            return Observable.Return(string.Empty);
        }

        public void Dispose() => _channel?.Dispose();

        // Pass-through delivery: each received event is delivered immediately to the
        // subscriber the channel emits to. Same shape as the test-side Postman in
        // RedisStreamBackingChannelBehaviour.
        private class PassthroughDelivery<T> : IDeliveryScheme<T>
        {
            public void Deliver(T @event, Action<T> postBox) => postBox(@event);
            public IObservable<T> Deliver(T @event, Func<T, IObservable<T>> postBox) => postBox(@event);
        }
    }
}
