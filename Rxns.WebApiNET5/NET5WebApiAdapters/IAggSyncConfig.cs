using System;

namespace Rxns.WebApiNET5.NET5WebApiAdapters
{
    public interface IEventSyncConfiguration
    {
        TimeSpan EventSyncInterval { get; }
        bool StaggerSyncRequests { get; }
    }

    public interface IAggSyncConfig : IEventSyncConfiguration
    {
        string SourceSqlSever { get; }
        string DestinationSqlSever { get; }
        string UserName { get; }
        string Password { get; }
    }
}
