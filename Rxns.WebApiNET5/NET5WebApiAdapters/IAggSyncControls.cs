using System;
using Rxns.DDD.BoundedContext;

namespace Rxns.WebApiNET5.NET5WebApiAdapters
{
    public class ForgetRequest
    {
        public string[] AggIds { get; set; }
        public string Type { get; set; }

    }
    public class SyncRequest : ForgetRequest
    {
        public string Level { get; set; }
        public Guid? LastEventId { get; set; }
    }

    /// <summary>
    /// The status of a sync connection
    /// </summary>
    public enum SyncState
    {
        /// <summary>
        /// A sync has been been requested, but the request is
        /// waiting in a queue to be serviced and the client is out
        /// of date
        /// </summary>
        Waiting,
        /// <summary>
        /// The events that have resulted from a sync request
        /// have started to be sent down the toSync channel
        /// </summary>
        Syncing,
        /// <summary>
        /// The client has successfully confirmed all aggregate events that they wanted
        /// to sync and no more events exist on the server
        /// </summary>
        UpToDate,
        /// <summary>
        /// Something went majorly wrong with the sync connection
        /// </summary>
        Aborted
    }

    public interface IAggSyncControls : IDisposable
    {
        /// <summary>
        /// The status of the sync connection, to determine how up to date the client is
        /// </summary>
        /// <returns></returns>
        IObservable<SyncState> SyncStatus();
        /// <summary>
        /// The events that the client has requested to be synced. You should listen to this channel
        /// before requesting something to be synced to ensure you dont miss anything
        /// </summary>
        /// <returns></returns>
        IObservable<IDomainEvent> ToSync();
        /// <summary>
        /// starts syncing a agg at a certain level
        /// </summary>
        /// <param name="aggIds"></param>
        /// <param name="level"></param>
        /// <returns></returns>
        void Sync(string[] aggIds, string type, string level, Guid? lastEventId = null);
        /// <summary>
        /// Forgets a agg you are already syncing
        /// note: will not throw if the idsubmitted has not been marked for sync 
        /// </summary>
        /// <param name="aggIds"></param>
        /// <returns></returns>
        void Forget(string type, params string[] aggIds);

        void Confirm(params Guid[] eventIds);
    }
}
