﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        public const string CACHE_KEY_SYSTEM_STATUS = "SystemStatus";
        public const string CACHE_KEY_SYSTEM_LOG = "SystemLog";
        public const string CACHE_KEY_COMMANDS = "CMD_";

        public IDictionary<object, object> Cache { get; private set; }


        public InMemoryAppStatusStore()
        {
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
        
        public string SaveLog(Stream log, string file)
        {
            if (!Directory.Exists("TenantLogs"))
                Directory.CreateDirectory("TenantLogs");

            using (var nextLog = File.Create(Path.Combine("TenantLogs", file)))
            {
                log.CopyTo(nextLog);
            }

            return file;
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
}
