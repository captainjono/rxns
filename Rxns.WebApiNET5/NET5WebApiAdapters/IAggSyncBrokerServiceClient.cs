using System;
using Rxns.DDD.BoundedContext;

namespace Rxns.WebApiNET5.NET5WebApiAdapters
{
    public interface IAggSyncBrokerClient
    {
        IObservable<IAggSyncControls> RegisterSlave(string tenant, string userName, string clientId);
    }

    public enum SyncConnection
    {
        Connecting,
        Connected,
        Reconnecting,
        Disconnected
    }

    public interface IAggSyncBrokerServiceClient : IAggSyncControls
    {
        void RegisterAsSlave(string clientId);
        void Sync(string[] aggIds, AggregateType type, AggSyncLevel level, Guid? lastEventId);
        IObservable<IDomainEvent> ToSync(string clientId);
        void Unregister(string clientId);
        void Forget(AggregateType type, string[] aggIds);
        void Confirm(Guid[] eventIds);
    }
}
