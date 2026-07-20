using System;
using System.Collections.Generic;

namespace Rxns.Health.AppStatus
{
    public class AppStatusLogEntry
    {
        public DateTime Timestamp { get; set; }
        public string Level { get; set; }
        public string Reporter { get; set; }
        public string SystemName { get; set; }
        public string Message { get; set; }
        public string StackTrace { get; set; }
        public string ErrorId { get; set; }
    }

    public class AppStatusLogPage
    {
        public IReadOnlyList<AppStatusLogEntry> Entries { get; set; }
        public int Total { get; set; }
        public int Skip { get; set; }
        public int Take { get; set; }
        public bool HasMore { get; set; }
    }

    public class AppStatusLogStats
    {
        public string SystemName { get; set; }
        public int ErrorsLast1h { get; set; }
        public int ErrorsLast24h { get; set; }
        public int WarningsLast1h { get; set; }
        public int WarningsLast24h { get; set; }
        public int InfoLast1h { get; set; }
        public int InfoLast24h { get; set; }
        public int TotalEntries { get; set; }
        public DateTime? LastSeenAt { get; set; }
    }

    /// <summary>
    /// Generic read surface over the AppStatus server's in-memory log + error buffers.
    /// Any reporter that publishes through <c>ReportStatus.Log</c> shows up here.
    /// Filtered by SystemName so portals can attach to one "app" view at a time.
    /// </summary>
    public interface IAppStatusLogReader
    {
        IReadOnlyList<string> GetRegisteredSystems();
        AppStatusLogPage GetLog(string systemName = null, string level = null, DateTime? since = null, int skip = 0, int take = 200);
        AppStatusLogPage GetErrors(string systemName = null, DateTime? since = null, int skip = 0, int take = 200);
        AppStatusLogStats GetStats(string systemName = null);
    }
}
