using Rxns.Interfaces;

namespace Rxns.Redis
{
    /// <summary>
    /// Worker -> hub: voluntary unregister on graceful shutdown.
    ///
    /// <para>The ONLY thing that removes a route. There is no passive counterpart any more - the
    /// hub used to evict any route whose heartbeat had not been seen inside a deadline, which
    /// turned a backlogged stream into a fleet that evicted itself and three rungs that finished
    /// partial without reporting an error. See RouteLifetimeBehaviour.</para>
    /// </summary>
    public class WorkerRouteRemoved : IRxn
    {
        public string Route { get; set; }
    }

    /// <summary>
    /// Worker -> hub: periodic "still here" beacon, and the thing that registers a route in the
    /// first place - the hub registers any heartbeat it does not recognise.
    ///
    /// <para>Arriving late costs a worker nothing. Nothing expires on the last-seen timestamp the
    /// hub keeps; it is held for diagnostics and to notice a respawned worker carrying a new
    /// clientId on a route it already knows.</para>
    ///
    /// <para>Cadence is ~5s. Do not read that as how quickly a loss is noticed: on a backlogged
    /// arena these were measured ARRIVING at 22-25s with a 374s tail, which is why nothing is
    /// inferred from their absence.</para>
    /// </summary>
    public class WorkerHeartbeat : IRxn
    {
        public string ClientId { get; set; }
        public string Route { get; set; }
    }
}
