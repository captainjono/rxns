using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Rxns.Cloud;
using Rxns.Hosting;
using Rxns.Logging;

namespace Rxns.AppStatus.Host.Discovery
{
    public class SsdpPeerEntry
    {
        public string Name { get; set; }
        public string Url { get; set; }
        public string FriendlyName { get; set; }
        public DateTime LastSeenUtc { get; set; }
    }

    public class SsdpPeerCache
    {
        private readonly ConcurrentDictionary<string, SsdpPeerEntry> _byUrl = new ConcurrentDictionary<string, SsdpPeerEntry>(StringComparer.OrdinalIgnoreCase);
        private IDisposable _subscription;
        private readonly object _gate = new object();

        public IReadOnlyList<SsdpPeerEntry> Snapshot(TimeSpan ttl)
        {
            var cutoff = DateTime.UtcNow - ttl;
            var stale = _byUrl.Where(kv => kv.Value.LastSeenUtc < cutoff).Select(kv => kv.Key).ToList();
            foreach (var k in stale) _byUrl.TryRemove(k, out _);
            return _byUrl.Values.ToList();
        }

        public void Start()
        {
            lock (_gate)
            {
                if (_subscription != null) return;
                try
                {
                    var disco = new SsdpDiscoveryService();
                    _subscription = disco.Discover().Subscribe(hb =>
                    {
                        if (string.IsNullOrWhiteSpace(hb?.Url)) return;
                        _byUrl[hb.Url] = new SsdpPeerEntry
                        {
                            Name = hb.Name,
                            Url = hb.Url,
                            FriendlyName = hb.FriendlyName,
                            LastSeenUtc = DateTime.UtcNow
                        };
                    },
                    e => ("ssdp discover error: " + e.Message).LogDebug("SsdpPeerCache"));
                }
                catch (Exception e)
                {
                    ("ssdp start failed: " + e.Message).LogDebug("SsdpPeerCache");
                }
            }
        }

        public void Stop()
        {
            lock (_gate)
            {
                try { _subscription?.Dispose(); } catch { }
                _subscription = null;
            }
        }
    }
}
