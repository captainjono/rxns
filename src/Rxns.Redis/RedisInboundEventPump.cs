using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Rxns.DDD.Commanding;
using Rxns.Health;
using Rxns.Hosting;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns.Redis
{
    /// <summary>
    /// Bridges Redis-side cross-process events onto the local IRxnManager bus.
    ///
    /// <para>
    /// Under lossless mode, <see cref="RedisAppStatusServiceClient"/> writes
    /// every published event onto the host-supplied typed-events stream. But its
    /// <see cref="RedisAppStatusServiceClient.Incoming"/> observable - the
    /// inbound side that delivers events from OTHER processes - was never
    /// subscribed to anywhere, so heartbeats / route registrations / typed
    /// events from workers never surfaced on the arena's local bus. The
    /// existing single-worker tests passed because SignalR's bridge was also
    /// running and quietly delivered the same events; multi-worker exposed
    /// the gap because SignalR can carry only one connection's events
    /// through to the same hub instance.
    /// </para>
    ///
    /// <para>
    /// This service does the missing wiring: subscribe Incoming, publish each
    /// event onto the local IRxnManager. From there RedisEventHub's
    /// subscriptions (<see cref="WorkerHeartbeat"/>, <see cref="WorkerRouteRemoved"/>)
    /// fire normally, and any other component subscribing on the local bus
    /// for typed events (UnitTestResult, RemoteShellResult, ...) sees them
    /// the same way it sees SignalR-delivered events.
    /// </para>
    ///
    /// <para>
    /// Implements <see cref="IRxnService"/> so PostBuildRxnServiceCreator
    /// activates it eagerly at boot - same pattern as RedisEventHub /
    /// RedisRoutedCmdConsumer. Without this, nothing would resolve it and the
    /// pump would never start.
    /// </para>
    /// </summary>
    public class RedisInboundEventPump : ReportsStatus, IRxnService, IDisposable
    {
        private readonly IAppStatusServiceClient _appStatus;
        private readonly IRxnManager<IRxn> _localBus;
        private CompositeDisposable _resources = new();

        public RedisInboundEventPump(IAppStatusServiceClient appStatus, IRxnManager<IRxn> localBus)
        {
            _appStatus = appStatus;
            _localBus = localBus;
        }

        public IObservable<CommandResult> Start(string from = null, string options = null)
        {
            // Only Redis-backed clients expose an Incoming observable; SignalR
            // / HTTP / NoOp variants don't, so the pump is a no-op for them.
            // The Redis variant's Incoming property triggers Setup on first
            // access (starting the poll loop) - that's why this is the place
            // we tap, not the constructor.
            if (_appStatus is RedisAppStatusServiceClient redisClient)
            {
                OnInformation("RedisInboundEventPump: piping RedisAppStatusServiceClient.Incoming -> local IRxnManager");
                var sub = redisClient.Incoming
                    .Subscribe(rxn =>
                    {
                        try
                        {
                            // Publish onto the local bus so type-based
                            // subscriptions (RedisEventHub.WorkerHeartbeat,
                            // command handlers, UI bridges) fire as if the
                            // event had arrived in-process. .Until handles
                            // any errors from downstream subscribers without
                            // killing the pump.
                            _localBus.Publish(rxn).Until(e => OnError(new Exception("Local bus republish failed", e)));
                        }
                        catch (Exception ex)
                        {
                            OnError(new Exception("RedisInboundEventPump dispatch failed", ex));
                        }
                    });
                _resources.Add(sub);
            }
            else
            {
                OnInformation("RedisInboundEventPump: IAppStatusServiceClient is {0} (not RedisAppStatusServiceClient) - pump is a no-op",
                    _appStatus?.GetType().Name ?? "<null>");
            }

            return Observable.Return(CommandResult.Success("RedisInboundEventPump started"));
        }

        public IObservable<CommandResult> Stop(string from = null)
        {
            Dispose();
            return Observable.Return(CommandResult.Success("RedisInboundEventPump stopped"));
        }

        public IObservable<CommandResult> Setup() =>
            Observable.Return(CommandResult.Success("RedisInboundEventPump setup"));

        public new void Dispose()
        {
            _resources?.Dispose();
        }
    }
}
