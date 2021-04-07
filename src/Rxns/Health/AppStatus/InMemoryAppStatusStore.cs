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
        
        public IObservable<string> SaveLog(string tenantId, Stream log, string file)
        {
            if (!Directory.Exists("TenantLogs"))
                Directory.CreateDirectory("TenantLogs");

            var logId = $"{tenantId}/{file}";
            var destinationDir = Path.Combine("TenantLogs", logId);


            if (_cfg.ShouldAutoUnzipLogs)
            {
                destinationDir = destinationDir.Substring(0, destinationDir.Length - 4);

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
            else
            {
                //todo: makeasync
                using (var f = File.Open($"{destinationDir}", FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Delete))
                {
                    log.CopyTo(f);
                }
            }

            return file.ToObservable();
        }

        public IObservable<Stream> GetLogs(string tenantId, string file)
        {
            if (!Directory.Exists("TenantLogs"))
            {
                Directory.CreateDirectory("TenantLogs");
            }

            return Rxn.Create(() => _fs.GetReadableFile(_fs.PathCombine("TenantLogs", $"{tenantId}-{file}")))
                .Catch<Stream, Exception>(e =>
                {
                    ReportStatus.Log.OnWarning($"While downloading update {e}");
                    return Observable.Return(new MemoryStream());
                });
        }


        public IObservable<AppLogInfo[]> ListLogs(string tenantId, int top = 3)
        {
            if (!_fs.ExistsDirectory(_fs.PathCombine("TenantLogs")))
                return Rxn.Empty<AppLogInfo[]>();

            return _fs.GetFiles("TenantLogs",
                tenantId.IsNullOrWhiteSpace("all").Equals("all", StringComparison.OrdinalIgnoreCase)
                    ? "*.zip"
                    : $"{tenantId}-*.zip").OrderByDescending(f => f.LastWriteTime).Take(top).Select(f =>
            {
                
                return new AppLogInfo()
                { 
                    Name = f.Name
                };
            }).ToArray().ToObservable().Catch<AppLogInfo[], Exception>(_ => Rxn.Empty<AppLogInfo[]>());
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

            current.Enqueue(IReportStatusExtensions.FromMessage(message as dynamic));

            Cache[CACHE_KEY_SYSTEM_LOG] = current;
        }

        private readonly object _cacheKey = new object();

        public IEnumerable<IRxnQuestion> FlushCommands(string route)
        {
            var tenantKey = GetCommandKey(route);


            lock (_cacheKey)
            {
                if (Cache.ContainsKey(tenantKey))
                {
                    var cmds = (Cache[tenantKey] as List<IRxnQuestion>).Where(cmd => cmd.IsFor(route)).ToArray();
                    //remove from cache
                    Cache.Remove(tenantKey);

                    return cmds;
                }
            }


            return new IRxnQuestion[] { };
        }

        public string GetCommandKey(string tenant)
        {
            return String.Format("{0}{1}", CACHE_KEY_COMMANDS, tenant).AsRoute();
        }

        public void Add(IRxnQuestion cmd)
        {
            var tenantSplit = cmd.Destination.Split('\\');
            if (tenantSplit.Length < 2) throw new ArgumentException(@"Must be of the format route\destinationsystem", "cmd.Destination");

            var tenantKey = GetCommandKey(String.Format("{0}\\{1}", tenantSplit[0], tenantSplit[1]));

            if (!Cache.ContainsKey(tenantKey))
            {
                Cache.Add(tenantKey, new List<IRxnQuestion>(new[] { cmd }));
            }
            else
            {
                var current = Cache[tenantKey] as List<IRxnQuestion>;
                current.Add(cmd);
            }
        }
    }

    public class AppLogInfo
    {
        public string Name { get; set; }
    }
}
