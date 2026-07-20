using System;

namespace Rxns.AppStatus.Host.Monitor
{
    /// <summary>
    /// Composable stream of observations fed into <see cref="MonitorService"/>.
    /// One source per signal kind: rxns bus log entries, infra probe state
    /// changes, AppInsights perf spikes, etc.
    ///
    /// <para>Sources are registered into DI just like <see cref="Rxns.Ai.IAiToolHandler"/>
    /// — augmentation modules (a consumer app's support portal) add their
    /// own without touching framework code. The UI
    /// renders one checkbox per source so the operator picks which streams
    /// feed monitor mode.</para>
    /// </summary>
    public interface IMonitorSource
    {
        /// <summary>Stable id used in subscriptions, the UI checkbox state, and
        /// dedupe keys (e.g. "bus-log", "infra-probe", "appinsights-perf").</summary>
        string Id { get; }

        /// <summary>Human-readable label for the source checkbox.</summary>
        string Label { get; }

        /// <summary>One-line description shown next to the checkbox in the UI.</summary>
        string Description { get; }

        /// <summary>False when the source can't run — backend unreachable,
        /// no config, etc. UI greys out the checkbox.</summary>
        bool IsAvailable { get; }

        /// <summary>Cold observable of monitor events. <see cref="MonitorService"/>
        /// subscribes when the source is enabled and disposes when disabled.</summary>
        IObservable<MonitorEvent> Events { get; }
    }
}
