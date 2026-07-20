using Rxns.Interfaces;

namespace Rxns.Redis
{
    /// <summary>
    /// Worker -> arena: voluntary unregister on graceful shutdown. Stale-route
    /// eviction also happens passively via heartbeat-watcher; this is the
    /// proactive path so the arena's _routes drops the entry immediately.
    /// </summary>
    public class WorkerRouteRemoved : IRxn
    {
        public string Route { get; set; }
    }

    /// <summary>
    /// Worker -> arena: periodic "still here" beacon. RedisEventHub records
    /// last-seen timestamp per route; routes not heartbeating within the
    /// configured window get evicted (lifecycle disconnect emit). Emit cadence
    /// matches SignalR's KeepAliveInterval (~5-10s) so a worker disappearing
    /// is detected within a comparable timeframe.
    /// </summary>
    public class WorkerHeartbeat : IRxn
    {
        public string ClientId { get; set; }
        public string Route { get; set; }
    }
}
