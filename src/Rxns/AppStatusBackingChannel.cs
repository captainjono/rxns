using System;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Rxns.Health;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns
{
    public class AppStatusBackingChannel : ReportsStatus, IRxnBackingChannel<IRxn>
    {
        private readonly Subject<IRxn> _publishBuffer = new Subject<IRxn>();

        /// <summary>
        ///
        /// </summary>
        /// <param name="eventService"></param>
        /// <param name="forEventsToCollectInBufffer">The amount of time to wait for events that are published to the backing channel before they are shipped off to the events service. default is 2 seconds</param>
        /// <param name="forBuffer">The scheduler used for the event collection buffer</param>
        public AppStatusBackingChannel(IAppStatusServiceClient eventService, TimeSpan? forEventsToCollectInBufffer = null, IScheduler forBuffer = null)
        {
            _publishBuffer.Buffer(forEventsToCollectInBufffer ?? TimeSpan.FromSeconds(2), forBuffer ?? Scheduler.Default).Where(l => l.Count > 0).Subscribe(this, e =>
            {
                // LogDebug, not OnVerbose. This channel reports into the very manager it backs, so
                // anything it says through the reporter chain goes nowhere a human will look - and
                // this is the only line in the process that says whether events are leaving for the
                // other side at all. Its silence used to be indistinguishable from a cluster with
                // nothing to say, which is how a worker that shipped no results for a whole run
                // looked exactly like a worker that had finished its work.
                $"AppStatusBackingChannel: shipping {e.Count} event(s) central: {string.Join(", ", e.Select(x => x.GetType().Name).Distinct())}".LogDebug();
                eventService.Publish(e).Subscribe();
            }).DisposedBy(this);
        }

        public IObservable<IRxn> Setup(IDeliveryScheme<IRxn> postman)
        {
            OnWarning("This backing channel is publish only");

            return Observable.Empty<IRxn>();
        }

        public void Publish(IRxn message)
        {
            _publishBuffer.OnNext(message);
        }
    }
}
