using System;
using System.Collections.Generic;
using System.Linq;
using Rxns.Metrics;

namespace Rxns.Health.AppStatus
{
    /// <summary>
    /// Default in-memory IAppStatusLogReader. Wraps InMemoryAppStatusStore.GetLog()
    /// (a CircularBuffer of SystemLogMeta, newest first) and IAppErrorManager.GetOutstandingErrors.
    /// Filters in-memory by SystemName — entries with null SystemName are returned only
    /// when the caller passes null too (i.e. "unscoped" view).
    /// </summary>
    public class LocalAppStatusLogReader : IAppStatusLogReader
    {
        private readonly InMemoryAppStatusStore _store;
        private readonly IAppErrorManager _errors;

        public LocalAppStatusLogReader(InMemoryAppStatusStore store, IAppErrorManager errors)
        {
            _store = store;
            _errors = errors;
        }

        public IReadOnlyList<string> GetRegisteredSystems()
        {
            var fromLog = AllEntries()
                .Where(e => !string.IsNullOrWhiteSpace(e.SystemName))
                .Select(e => e.SystemName);

            var fromErrors = SafeErrors(null)
                .Where(e => !string.IsNullOrWhiteSpace(e.System))
                .Select(e => e.System);

            return fromLog.Concat(fromErrors)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public AppStatusLogPage GetLog(string systemName = null, string level = null, DateTime? since = null, int skip = 0, int take = 200)
        {
            var q = AllEntries();
            q = Filter(q, systemName, level, since);
            return Paginate(q, skip, take);
        }

        public AppStatusLogPage GetErrors(string systemName = null, DateTime? since = null, int skip = 0, int take = 200)
        {
            // Pull from log first (typed Errors are flagged Level="Error" or "Fatal").
            var fromLog = AllEntries()
                .Where(e => string.Equals(e.Level, "Error", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(e.Level, "Fatal", StringComparison.OrdinalIgnoreCase));

            // Merge with IAppErrorManager.GetOutstandingErrors so historical/persisted errors show up too.
            var fromErrors = SafeErrors(systemName)
                .Select(e => new AppStatusLogEntry
                {
                    Timestamp = e.Timestamp,
                    Level = "Error",
                    Reporter = e.System,
                    SystemName = e.System,
                    Message = e.Error,
                    StackTrace = e.StackTrace,
                    ErrorId = e.ErrorId
                });

            var merged = fromLog.Concat(fromErrors);
            merged = Filter(merged, systemName, level: null, since: since);
            return Paginate(merged, skip, take);
        }

        public AppStatusLogStats GetStats(string systemName = null)
        {
            var now = DateTime.UtcNow;
            var oneHourAgo = now.AddHours(-1);
            var oneDayAgo = now.AddHours(-24);

            var scoped = AllEntries();
            if (!string.IsNullOrWhiteSpace(systemName))
                scoped = scoped.Where(e => string.Equals(e.SystemName, systemName, StringComparison.OrdinalIgnoreCase));

            var snapshot = scoped.ToList();

            return new AppStatusLogStats
            {
                SystemName = systemName,
                ErrorsLast1h = snapshot.Count(e => IsErr(e) && e.Timestamp >= oneHourAgo),
                ErrorsLast24h = snapshot.Count(e => IsErr(e) && e.Timestamp >= oneDayAgo),
                WarningsLast1h = snapshot.Count(e => IsWarn(e) && e.Timestamp >= oneHourAgo),
                WarningsLast24h = snapshot.Count(e => IsWarn(e) && e.Timestamp >= oneDayAgo),
                InfoLast1h = snapshot.Count(e => IsInfo(e) && e.Timestamp >= oneHourAgo),
                InfoLast24h = snapshot.Count(e => IsInfo(e) && e.Timestamp >= oneDayAgo),
                TotalEntries = snapshot.Count,
                LastSeenAt = snapshot.Count > 0 ? snapshot.Max(e => e.Timestamp) : (DateTime?)null
            };
        }

        private IEnumerable<AppStatusLogEntry> AllEntries()
        {
            // GetLog() returns IEnumerable<object> over SystemLogMeta in
            // INSERTION order (oldest first — CircularBuffer.Contents() walks the
            // backing array directly). Portal consumers expect newest-first for
            // log dashboards, so reverse it here. Materialising to a List is
            // safe — the buffer caps at 3500.
            IEnumerable<object> raw;
            try { raw = _store?.GetLog() ?? Enumerable.Empty<object>(); }
            catch { return Enumerable.Empty<AppStatusLogEntry>(); }

            var ordered = raw.OfType<SystemLogMeta>().ToList();
            ordered.Reverse();
            return ordered.Select(Map);
        }

        private static AppStatusLogEntry Map(SystemLogMeta m)
        {
            return new AppStatusLogEntry
            {
                Timestamp = m.Timestamp,
                Level = m.Level,
                Reporter = m.Reporter,
                SystemName = m.SystemName,
                Message = m.Message,
                StackTrace = m.StackTrace,
                ErrorId = m.ErrorId > 0 ? m.ErrorId.ToString() : null
            };
        }

        private IEnumerable<SystemErrors> SafeErrors(string systemName)
        {
            try
            {
                return _errors?.GetOutstandingErrors(page: 0, size: 500, tenant: null, systemName: systemName)
                       ?? Enumerable.Empty<SystemErrors>();
            }
            catch
            {
                return Enumerable.Empty<SystemErrors>();
            }
        }

        private static IEnumerable<AppStatusLogEntry> Filter(IEnumerable<AppStatusLogEntry> src, string systemName, string level, DateTime? since)
        {
            if (!string.IsNullOrWhiteSpace(systemName))
                src = src.Where(e => string.Equals(e.SystemName, systemName, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(level))
                src = src.Where(e => string.Equals(e.Level, level, StringComparison.OrdinalIgnoreCase));

            if (since.HasValue)
                src = src.Where(e => e.Timestamp >= since.Value);

            return src;
        }

        private static AppStatusLogPage Paginate(IEnumerable<AppStatusLogEntry> src, int skip, int take)
        {
            var snapshot = src.ToList();
            var total = snapshot.Count;

            if (skip < 0) skip = 0;
            if (take <= 0) take = 200;
            if (take > 1000) take = 1000;

            var page = snapshot.Skip(skip).Take(take).ToList();

            return new AppStatusLogPage
            {
                Entries = page,
                Total = total,
                Skip = skip,
                Take = take,
                HasMore = skip + page.Count < total
            };
        }

        private static bool IsErr(AppStatusLogEntry e) =>
            string.Equals(e.Level, "Error", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(e.Level, "Fatal", StringComparison.OrdinalIgnoreCase);

        private static bool IsWarn(AppStatusLogEntry e) =>
            string.Equals(e.Level, "Warning", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(e.Level, "Warn", StringComparison.OrdinalIgnoreCase);

        private static bool IsInfo(AppStatusLogEntry e) =>
            string.Equals(e.Level, "Information", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(e.Level, "Info", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(e.Level, "Verbose", StringComparison.OrdinalIgnoreCase);
    }
}
