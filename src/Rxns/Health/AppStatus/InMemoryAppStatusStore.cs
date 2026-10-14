using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using Rxns.Collections;
using Rxns.DDD.Commanding;
using Rxns.Hosting.Updates;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns.Health.AppStatus
{

    public class InMemoryAppStatusStore : IAppStatusStore
    {
        private readonly IFileSystemService _fs;
        private readonly IAppStatusCfg _cfg;
        private readonly IZipService _zipService;


        public const string CACHE_KEY_SYSTEM_STATUS = "SystemStatus";
        public const string CACHE_KEY_SYSTEM_LOG = "SystemLog";
        public const string CACHE_KEY_COMMANDS = "CMD_";

        public IDictionary<object, object> Cache { get; private set; }


        public InMemoryAppStatusStore(IFileSystemService fs, IAppStatusCfg cfg, IZipService zipService)
        {
            _fs = fs;
            _cfg = cfg;
            _zipService = zipService;
            Clear();

            LogDir = Path.Combine(_cfg.AppRoot, "TenantLogs");
        }

        public IDictionary<string, Dictionary<SystemStatusEvent, object[]>> GetSystemStatus()
        {
            return Cache[CACHE_KEY_SYSTEM_STATUS] as Dictionary<string, Dictionary<SystemStatusEvent, object[]>>;
        }

        public void Clear()
        {
            Cache = Cache ?? new Dictionary<object, object>();
            Cache.Clear();

            Cache.Add(CACHE_KEY_SYSTEM_STATUS, new Dictionary<string, Dictionary<SystemStatusEvent, object[]>>());
            Cache.Add(CACHE_KEY_SYSTEM_LOG, new CircularBuffer<object>(3500));
        }

        public void ClearSystemStatus(string route)
        {
            var cache = (Cache[CACHE_KEY_SYSTEM_STATUS] as Dictionary<string, Dictionary<SystemStatusEvent, object[]>>);
            var tenantCache = cache.Keys.FirstOrDefault(s => s.AsRoute().Equals(route.AsRoute()));

            if (tenantCache == null)
                throw new ArgumentException(String.Format(@"Database with route '{0}' not found. Ensure format is route\destinationsystem", route), "route");

            cache.Remove(tenantCache);
        }

        public IEnumerable<object> GetLog()
        {
            return (Cache[CACHE_KEY_SYSTEM_LOG] as CircularBuffer<object>).Contents();
        }

        public string LogDir { get; set; }
        
        /// <summary>
        /// Where a tenant's uploaded artefacts live. One directory per tenant, so a run is
        /// addressable by the name it was uploaded under in either storage mode.
        /// </summary>
        private string TenantDir(string tenantId)
        {
            var dir = Path.Combine(LogDir, tenantId ?? "");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return dir;
        }

        public IObservable<string> SaveLog(string tenantId, Stream log, string file)
        {
            var tenantDir = TenantDir(tenantId);
            var destination = Path.Combine(tenantDir, file);

            if (!_cfg.ShouldAutoUnzipLogs)
            {
                // Stored as it arrived. The upload is already a compressed archive and every
                // reader of it - the report aggregator, the portal, a later export - can read it
                // that way, so expanding it buys nothing and costs the difference between the
                // archive and its contents on a disk the host shares with the run.
                //
                // Create rather than CreateNew: an upload that failed part-way is retried, and
                // CreateNew turns the retry into a permanent failure over the partial file it
                // just wrote. Names carry the system, the run and a millisecond stamp, so the
                // only realistic collision is an upload with itself.
                using (var f = File.Open(destination, FileMode.Create, FileAccess.ReadWrite, FileShare.Delete))
                {
                    log.CopyTo(f);
                }

                return file.ToObservable();
            }

            var destinationDir = destination.Substring(0, destination.Length - 4);
            Directory.CreateDirectory(destinationDir);

            return _zipService.GetFiles(log)
                .Do(f =>
                {
                    //todo: makeasync
                    using (var fl = File.Open(Path.Combine(destinationDir, f.Name), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Delete))
                    {
                        f.Contents.CopyTo(fl);
                    }
                })
                .LastOrDefaultAsync().Select(_ => file);
        }

        public IObservable<Stream> GetLogs(string tenantId, string file)
        {
            return Rxn.Create(() => _fs.GetReadableFile(_fs.PathCombine(TenantDir(tenantId), file)))
                .Catch<Stream, Exception>(e =>
                {
                    ReportStatus.Log.OnWarning($"While downloading update {e}");
                    return Observable.Return(new MemoryStream());
                });
        }


        public IObservable<AppLogInfo[]> ListLogs(string tenantId, int top = 3)
        {
            if (!_fs.ExistsDirectory(LogDir))
                return Rxn.Empty<AppLogInfo[]>();

            // A run is a .zip when the store keeps uploads as they arrive and a directory when it
            // expands them, so both are listed. Names are what SaveLog wrote, which is what
            // GetLogs takes back.
            var tenants = tenantId.IsNullOrWhiteSpace("all").Equals("all", StringComparison.OrdinalIgnoreCase)
                ? Directory.GetDirectories(LogDir)
                : new[] { TenantDir(tenantId) };

            return tenants
                .Where(Directory.Exists)
                .SelectMany(t => Directory.GetFileSystemEntries(t))
                .Select(e => new FileInfo(e))
                .OrderByDescending(f => f.LastWriteTime)
                .Take(top)
                .Select(f => new AppLogInfo() { Name = f.Name })
                .ToArray()
                .ToObservable()
                .Catch<AppLogInfo[], Exception>(_ => Rxn.Empty<AppLogInfo[]>());
        }

        public void Add(LogMessage<string> message)
        {
            Log(message);
        }

        public void Add(LogMessage<Exception> message)
        {
            Log(message);
        }

        private void Log(object message)
        {
            var current = Cache[CACHE_KEY_SYSTEM_LOG] as CircularBuffer<object>;

            // Preserve cross-process attribution: when a remote RLM lands here
            // via RemoteReportStatusEcho, the originating system name has been
            // stuffed into the LogMessage.Reporter field (RLM.S → reporter).
            // The one-arg FromMessage(dynamic) overload hardcodes SystemName=null,
            // so every cross-process entry would be invisible to
            // /api/appstatus/log?systemName=… and /api/appstatus/systems. Parse
            // the reporter (stripping the "{ip}][" prefix EventController.Publish
            // adds at :45) and pass it as systemName to the two-arg overload.
            var systemName = ExtractSystemName(message);
            current.Enqueue(IReportStatusExtensions.FromMessage(message as dynamic, systemName));

            Cache[CACHE_KEY_SYSTEM_LOG] = current;
        }

        /// <summary>
        /// Pull originating systemName out of a LogMessage.Reporter populated
        /// by <see cref="Logging.RemoteReportStatusEcho"/>. The reporter has the
        /// shape <c>"{clientIp}][{systemName}"</c> after the EventController
        /// stamps the client IP; older entries are bare <c>"systemName"</c>.
        /// Returns null when no recognisable system name is present, so the
        /// in-process (non-remote) path keeps its existing SystemName=null
        /// behaviour.
        /// </summary>
        private static string ExtractSystemName(object message)
        {
            string reporter = null;
            try
            {
                dynamic dm = message;
                reporter = (string)dm.Reporter;
            }
            catch { /* not a LogMessage<T> — fall through */ }
            if (string.IsNullOrWhiteSpace(reporter)) return null;
            // After EventController.Publish (:45): "{ip}][{originalS}"
            // The "][" sentinel splits remote-injected ip from the original S.
            var ipDelim = reporter.IndexOf("][", StringComparison.Ordinal);
            if (ipDelim >= 0) reporter = reporter.Substring(ipDelim + 2);
            // The original S can be "{systemName}/{moduleReporter}" (the adapter's
            // RLM wrapping convention). Strip the trailing reporter to get the
            // bare system. Local in-process messages with plain reporters fall
            // through unchanged and end up as the systemName too — harmless because
            // their LocalAppStatusManager.ReportInformation already sets it.
            var slash = reporter.IndexOf('/');
            if (slash > 0) reporter = reporter.Substring(0, slash);
            return string.IsNullOrWhiteSpace(reporter) ? null : reporter.Trim();
        }

        // Add(IRxnQuestion) and FlushCommands(route) moved to
        // RoutableAppCmdManager (phase 7n4 refactor). Cmd routing +
        // queueing is the manager's concern; this store keeps only
        // log/cache/system-status responsibilities.
    }

    public class AppLogInfo
    {
        public string Name { get; set; }
    }
}
