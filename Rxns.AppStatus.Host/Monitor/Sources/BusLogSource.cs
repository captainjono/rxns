using System;
using System.Linq;
using System.Reactive.Linq;
using Newtonsoft.Json;
using Rxns.AppStatus.Host.Monitor;
using Rxns.Health.AppStatus;

namespace Rxns.AppStatus.Host.Monitor.Sources
{
    /// <summary>
    /// Default <see cref="IMonitorSource"/> that polls <see cref="IAppStatusLogReader"/>
    /// every few seconds for recent Error/Fatal entries and emits them as
    /// <see cref="MonitorEvent"/>s. Anything any registered reporter logs at
    /// Error level shows up here — including AppErrorManager entries surfaced
    /// via <see cref="IAppStatusLogReader.GetErrors"/>.
    ///
    /// <para>Polling rather than push-subscribing because the existing log
    /// pipeline doesn't expose an <c>IObservable&lt;AppStatusLogEntry&gt;</c>
    /// (it writes into an in-memory store via <c>InMemoryAppStatusStore</c>).
    /// 5s cadence is cheap — buffered window query, no SQL — and the AI
    /// prompt batching downstream lives at 60s scale anyway.</para>
    /// </summary>
    public class BusLogSource : IMonitorSource
    {
        private readonly IAppStatusLogReader _reader;
        private DateTime _lastSeenAt;

        public BusLogSource(IAppStatusLogReader reader)
        {
            _reader = reader;
            _lastSeenAt = DateTime.UtcNow;
        }

        public string Id => "bus-log";
        public string Label => "Rxns log bus (errors)";
        public string Description => "Live errors from anything publishing through ReportStatus.Log — apps, agents, the portal itself.";
        public bool IsAvailable => _reader != null;

        public IObservable<MonitorEvent> Events
        {
            get
            {
                return Observable
                    .Interval(TimeSpan.FromSeconds(5))
                    .SelectMany(_ => PullSinceLastSeen());
            }
        }

        private System.Collections.Generic.IEnumerable<MonitorEvent> PullSinceLastSeen()
        {
            if (_reader == null) yield break;

            AppStatusLogPage page = null;
            try { page = _reader.GetErrors(systemName: null, since: _lastSeenAt, skip: 0, take: 50); }
            catch { yield break; }

            var entries = page?.Entries;
            if (entries == null || entries.Count == 0) yield break;

            // Advance the watermark to the latest entry's timestamp so the next
            // tick doesn't re-emit the same rows. Defensive against same-ts
            // ties: bump by 1ms so a strict `> since` server-side wouldn't
            // double-fetch (the reader is inclusive `>= since`, but we want
            // the bump anyway to avoid surfacing the same evidence twice).
            var maxTs = entries.Max(e => e.Timestamp);
            if (maxTs > _lastSeenAt) _lastSeenAt = maxTs.AddMilliseconds(1);

            foreach (var entry in entries)
            {
                yield return new MonitorEvent
                {
                    SourceId = Id,
                    At = entry.Timestamp,
                    Category = "log-error",
                    Severity = string.Equals(entry.Level, "Fatal", StringComparison.OrdinalIgnoreCase) ? "error" : "error",
                    Title = TruncateOneLine(entry.Message, 120),
                    EvidenceJson = JsonConvert.SerializeObject(new
                    {
                        systemName = entry.SystemName,
                        reporter = entry.Reporter,
                        level = entry.Level,
                        timestamp = entry.Timestamp,
                        message = entry.Message,
                        stackTrace = TruncateOneLine(entry.StackTrace, 1200),
                        errorId = entry.ErrorId
                    }),
                    CorrelationKey = string.IsNullOrEmpty(entry.ErrorId) ? entry.SystemName + "|" + entry.Timestamp.Ticks : entry.ErrorId
                };
            }
        }

        private static string TruncateOneLine(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var oneLine = s.Replace("\r", " ").Replace("\n", " ");
            return oneLine.Length <= max ? oneLine : oneLine.Substring(0, max) + "…";
        }
    }
}
