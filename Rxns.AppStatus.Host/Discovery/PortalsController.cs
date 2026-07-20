using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace Rxns.AppStatus.Host.Discovery
{
    public static class PortalDiscovery
    {
        public static SsdpPeerCache SsdpCache { get; set; }
        public static string SelfUrl { get; set; }
        public static string SelfName { get; set; }
        public static string[] SelfAugments { get; set; } = Array.Empty<string>();
        public static string DataDir { get; set; }
    }

    public class CustomPeerEntry
    {
        public string Name { get; set; }
        public string Url { get; set; }
        public DateTime AddedAt { get; set; }
    }

    public class AddCustomPeerRequest
    {
        public string Name { get; set; }
        public string Url { get; set; }
    }

    [ApiController]
    [Route("api/portals")]
    public class PortalsController : ControllerBase
    {
        private static readonly object _fileGate = new object();

        private static string PeersFilePath()
        {
            var dir = PortalDiscovery.DataDir ?? AppContext.BaseDirectory;
            return Path.Combine(dir, "peers.json");
        }

        private static List<CustomPeerEntry> ReadCustomPeers()
        {
            var path = PeersFilePath();
            lock (_fileGate)
            {
                if (!System.IO.File.Exists(path)) return new List<CustomPeerEntry>();
                try
                {
                    var json = System.IO.File.ReadAllText(path);
                    if (string.IsNullOrWhiteSpace(json)) return new List<CustomPeerEntry>();
                    return JsonConvert.DeserializeObject<List<CustomPeerEntry>>(json) ?? new List<CustomPeerEntry>();
                }
                catch
                {
                    return new List<CustomPeerEntry>();
                }
            }
        }

        private static void WriteCustomPeers(List<CustomPeerEntry> peers)
        {
            var path = PeersFilePath();
            lock (_fileGate)
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                var tmp = path + ".tmp";
                System.IO.File.WriteAllText(tmp, JsonConvert.SerializeObject(peers, Formatting.Indented));
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
                System.IO.File.Move(tmp, path);
            }
        }

        private static string NormUrl(string u) => string.IsNullOrWhiteSpace(u) ? u : u.TrimEnd('/');

        private static object BuildPeerList()
        {
            var byUrl = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(PortalDiscovery.SelfUrl))
            {
                byUrl[NormUrl(PortalDiscovery.SelfUrl)] = new
                {
                    name = PortalDiscovery.SelfName,
                    url = PortalDiscovery.SelfUrl,
                    augments = PortalDiscovery.SelfAugments ?? Array.Empty<string>(),
                    current = true,
                    source = "self"
                };
            }

            if (PortalDiscovery.SsdpCache != null)
            {
                foreach (var p in PortalDiscovery.SsdpCache.Snapshot(TimeSpan.FromSeconds(60)))
                {
                    var key = NormUrl(p.Url);
                    if (byUrl.ContainsKey(key)) continue;
                    byUrl[key] = new
                    {
                        name = p.FriendlyName ?? p.Name,
                        url = p.Url,
                        augments = Array.Empty<string>(),
                        current = false,
                        source = "ssdp"
                    };
                }
            }

            foreach (var p in ReadCustomPeers())
            {
                if (string.IsNullOrWhiteSpace(p.Url)) continue;
                var key = NormUrl(p.Url);
                if (byUrl.ContainsKey(key)) continue;
                byUrl[key] = new
                {
                    name = p.Name,
                    url = p.Url,
                    augments = Array.Empty<string>(),
                    current = false,
                    source = "custom"
                };
            }

            return new { peers = byUrl.Values.ToArray() };
        }

        [HttpGet("peers")]
        public IActionResult Peers()
        {
            return Ok(BuildPeerList());
        }

        [HttpPost("peers/custom")]
        public IActionResult AddCustom([FromBody] AddCustomPeerRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.Url))
                return BadRequest(new { error = "name and url are required" });

            if (!Uri.TryCreate(req.Url, UriKind.Absolute, out var parsed))
                return BadRequest(new { error = "url is not a valid absolute uri" });

            if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
                return BadRequest(new { error = "url scheme must be http or https" });

            var peers = ReadCustomPeers();
            if (!peers.Any(p => string.Equals(p.Url, req.Url, StringComparison.OrdinalIgnoreCase)))
            {
                peers.Add(new CustomPeerEntry
                {
                    Name = req.Name,
                    Url = req.Url,
                    AddedAt = DateTime.UtcNow
                });
                WriteCustomPeers(peers);
            }

            return StatusCode(201, BuildPeerList());
        }

        [HttpDelete("peers/custom")]
        public IActionResult DeleteCustom([FromQuery] string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return BadRequest(new { error = "url query parameter is required" });

            var peers = ReadCustomPeers();
            var before = peers.Count;
            peers = peers.Where(p => !string.Equals(p.Url, url, StringComparison.OrdinalIgnoreCase)).ToList();
            if (peers.Count != before)
                WriteCustomPeers(peers);

            return Ok(BuildPeerList());
        }
    }
}
