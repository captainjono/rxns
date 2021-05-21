using System;
using Rxns.DDD.BoundedContext;

namespace Rxns.WebApiNET5.NET5WebApiAdapters
{
    public interface IAggPublishControls : IDisposable
    {
        /// <summary>
        /// a stream of events that should be synced with the broker
        /// </summary>
        /// <returns></returns>
        IObserver<IDomainEvent> ToSync();

        IObserver<SyncState> SyncStatus();
            /// <summary>
        /// Forgets a agg you are already syncing
        /// note: will not throw if the aggid submitted has not been marked for sync 
        /// </summary>
        /// <param name="aggids"></param>
        /// <returns></returns>
        IObservable<Guid> Confirmation(string[] aggIds);
    }

    public interface IAggSyncBroker : IAggSyncBrokerClient
    {
        IObservable<IAggPublishControls> RegisterMaster(string clientId, string tenant, string userName);
    }
}
