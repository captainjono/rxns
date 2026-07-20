using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Rxns.Health;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns
{
    /// <summary>
    /// Backing channel for the distributed central/centralReliable RxnManagers
    /// that publishes via SignalR (IAppStatusServiceClient is the
    /// SignalRRxnManagerBridge in the standard DI wiring) instead of HTTP.
    ///
    /// <para>
    /// Why this exists: AppStatusBackingChannel sends every published IRxn via
    /// <c>eventService.Publish</c> (HTTP POST to <c>/events/publish</c>). When
    /// the process hosting <c>EventsHub</c> is ALSO the process posting (i.e.
    /// a self-hosted arena), the POST lands on the arena's own hub, which
    /// re-publishes the event onto the local bus, the router then fans it back
    /// to the central manager, which POSTs again — infinite feedback loop.
    /// SignalR avoids the loop because the bridge's <c>publishChannel</c>
    /// forwards to a remote hub connection; on an arena with no upstream hub
    /// to forward to, the publish is a no-op, not a self-POST.
    /// </para>
    ///
    /// <para>
    /// Unlike AppStatusBackingChannel this is NOT buffered — SignalR already
    /// handles connection-state buffering inside SignalRRxnManagerBridge
    /// (pre-config buffer + reconnect semantics).
    /// </para>
    /// </summary>
    public class SignalRBackingChannel : ReportsStatus, IRxnBackingChannel<IRxn>
    {
        private readonly IAppStatusServiceClient _eventService;

        public SignalRBackingChannel(IAppStatusServiceClient eventService)
        {
            _eventService = eventService;
        }

        public IObservable<IRxn> Setup(IDeliveryScheme<IRxn> postman)
        {
            // This backing channel is publish-only. Inbound subscription to the
            // same SignalR connection is handled separately by
            // SignalRRxnManagerBridge.CreateSubscription.
            return Observable.Empty<IRxn>();
        }

        public void Publish(IRxn message)
        {
            _eventService.Publish(new[] { message }).Subscribe();
        }
    }
}
