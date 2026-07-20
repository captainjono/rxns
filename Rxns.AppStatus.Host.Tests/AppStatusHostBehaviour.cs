using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Net.Http;
using System.Net.Sockets;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Rxns;
using Rxns.Interfaces;
using Rxns.Logging;
using Rxns.Microservices;

namespace Rxns.AppStatus.Host.Tests
{
    /// <summary>
    /// In-process behaviour for <see cref="AppStatusPortal"/>: boot a portal on a free
    /// port, probe the contract endpoints, exercise the publish path that the
    /// rxns-support adapter consumes. Mirrors bfg's <c>RemoteClusterBehaviour</c>
    /// fixture-and-probe idiom — but in-process because the host is library-only.
    /// </summary>
    [TestClass]
    [TestCategory("Host")]
    public class AppStatusHostBehaviour
    {
        private static int _port;
        private static string _baseUrl;
        private static HttpClient _http;

        [ClassInitialize]
        public static async Task StartHost(TestContext ctx)
        {
            _port = FindFreePort();
            _baseUrl = "http://localhost:" + _port;

            var cfg = new AppStatusHostCfg
            {
                BindingUrl = _baseUrl,
                Html5Root  = AppStatusPortal.ResolveHtml5Root(),
                SystemName = "myapp"
            };
            _ = AppStatusPortal.StartAsync(cfg);

            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

            // Poll until Kestrel binds. ConfigureAndStartAspnetCore takes ~1.5-2.5s on a warm machine.
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    var r = await _http.GetAsync(_baseUrl + "/api/appstatus/systems");
                    if (r.IsSuccessStatusCode) return;
                }
                catch { await Task.Delay(200); }
            }
            throw new InvalidOperationException("Host did not become ready within 20s");
        }

        [ClassCleanup]
        public static void StopHost()
        {
            try { _http?.Dispose(); } catch { }
            try { AppStatusPortal.Stop(); } catch { }
        }

        [TestMethod]
        [Timeout(20 * 1000)]
        public async Task host_serves_the_appstatus_rest_surface()
        {
            (await _http.GetStringAsync(_baseUrl + "/api/appstatus/systems"))
                .TrimStart().Should().StartWith("[",
                    because: "the systems endpoint returns a JSON array of registered SystemNames");

            (await _http.GetStringAsync(_baseUrl + "/api/appstatus/log"))
                .Should().NotBeNull();

            // Server uses JsonNamingPolicy.CamelCase globally, so the wire is camelCase.
            var statsBody = (await _http.GetStringAsync(_baseUrl + "/api/appstatus/stats")).ToLowerInvariant();
            statsBody.Should().Contain("totalentries").And.Contain("errorslast1h",
                because: "stats returns the AppStatusLogStats shape (camelCase on the wire)");
        }

        [TestMethod]
        [Timeout(20 * 1000)]
        public async Task ai_info_advertises_all_read_only_tools()
        {
            // The endpoint was /api/claude/info before the Rxns.Claude → Rxns.Ai rename.
            var body = await _http.GetStringAsync(_baseUrl + "/api/ai/info");
            foreach (var name in new[] { "query_logs", "list_systems", "get_stats", "query_errors", "query_appinsights" })
            {
                body.Should().Contain(name, because: "the AI tool registry must advertise '" + name + "' even in read-only mode");
            }
            body.Should().Contain("readOnly").And.Contain("engines");
        }

        [TestMethod]
        [Timeout(20 * 1000)]
        public async Task appinsights_info_returns_targets_or_empty_array_when_no_cfg()
        {
            var body = await _http.GetStringAsync(_baseUrl + "/api/appinsights/info");
            body.Should().Contain("available")
                .And.Contain("targets")
                .And.Contain("presets",
                    because: "the info endpoint advertises configured targets + the preset KQL menu");
        }

        [TestMethod]
        [Timeout(20 * 1000)]
        public async Task portal_spa_assets_are_served_from_dist()
        {
            var index = await _http.GetStringAsync(_baseUrl + "/index.html");
            index.Should().Contain("app.full.min.js",
                because: "the built index.html injects the bundle script tag at the end of <body>");

            var bundle = await _http.GetStringAsync(_baseUrl + "/app.full.min.js");
            bundle.Length.Should().BeGreaterThan(100_000,
                because: "the production bundle includes Angular + all the portal modules + the new support partials");
        }

        [TestMethod]
        [Timeout(40 * 1000)]
        public async Task report_status_publish_surfaces_in_appstatus_log_endpoint()
        {
            var marker = "host-test-marker-" + Guid.NewGuid().ToString("N").Substring(0, 8);

            // Diagnostic: subscribe to ReportStatus.Log.Information directly from the test
            // thread so we can confirm whether the static publish is at least being received
            // by *somebody* in-process. If `directSeen` is false, the static surface itself
            // isn't firing for our reporter.
            var directSeen = false;
            var subscriberCount = ReportStatus.Log.ReportInformation.HasObservers ? "≥1" : "0";
            using var directSub = ReportStatus.Log.Information.Subscribe(m =>
            {
                if (m?.Message != null && m.Message.Contains(marker)) directSeen = true;
            });

            // ── Diagnostic: subscribe to the host's resolved IAppContainer.Information ──
            var containerSeen = false;
            IDisposable containerSub = null;
            string containerType = "<not resolved>";
            // Capture the store reference too so we can probe it directly later.
            Rxns.Health.AppStatus.InMemoryAppStatusStore probeStore = null;
            string probeStoreError = null;
            try
            {
                if (AppStatusHostDiagnostics.Resolver != null)
                {
                    var appContainer = AppStatusHostDiagnostics.Resolver.Resolve<IAppContainer>();
                    containerType = appContainer.GetType().FullName;
                    containerSub = appContainer.Information.Subscribe(m =>
                    {
                        if (m?.Message != null && m.Message.Contains(marker)) containerSeen = true;
                    });

                    try { probeStore = AppStatusHostDiagnostics.Resolver.Resolve<Rxns.Health.AppStatus.InMemoryAppStatusStore>(); }
                    catch (Exception ex) { probeStoreError = ex.GetType().Name + ": " + ex.Message; }
                }
            }
            catch (Exception ex)
            {
                containerType = "<resolve failed: " + ex.GetType().Name + ": " + ex.Message + ">";
            }

            // Canonical in-process publish surface — exactly what the adapter wraps for
            // cross-process HTTP. AppStatusServerModule's LocalAppStatusManager subscribes
            // to this stream and records into InMemoryAppStatusStore.
            ReportStatus.Log.OnInformation("host-test", marker);

            // Bus is async — poll for up to 30s for the marker to surface.
            var deadline = DateTime.UtcNow.AddSeconds(30);
            string lastBody = null;
            while (DateTime.UtcNow < deadline)
            {
                lastBody = await _http.GetStringAsync(_baseUrl + "/api/appstatus/log?take=500");
                if (lastBody.Contains(marker)) return;
                await Task.Delay(500);
            }

            // Drain container sub.
            try { containerSub?.Dispose(); } catch { }

            // Probe the store directly — bypass HTTP + reader so we see raw buffer contents.
            int probeStoreCount = -1;
            bool probeStoreHasMarker = false;
            string probeStoreReporters = "<unknown>";
            if (probeStore != null)
            {
                try
                {
                    var raw = probeStore.GetLog();
                    var list = new System.Collections.Generic.List<object>();
                    foreach (var o in raw) list.Add(o);
                    probeStoreCount = list.Count;
                    foreach (var o in list)
                    {
                        var meta = o as Rxns.Metrics.SystemLogMeta;
                        if (meta != null && meta.Message != null && meta.Message.Contains(marker))
                        {
                            probeStoreHasMarker = true; break;
                        }
                    }
                    var reps = new System.Collections.Generic.HashSet<string>();
                    foreach (var o in list)
                    {
                        var meta = o as Rxns.Metrics.SystemLogMeta;
                        if (meta != null && meta.Reporter != null) reps.Add(meta.Reporter);
                    }
                    probeStoreReporters = string.Join(",", reps);
                }
                catch (Exception ex) { probeStoreError = "iterate failed: " + ex.GetType().Name + ": " + ex.Message; }
            }

            // Print the diagnostic to console so it shows in test output even when
            // FluentAssertions truncates the `because:` message.
            Console.WriteLine($"[DIAG] directSub saw marker     = {directSeen}");
            Console.WriteLine($"[DIAG] containerSub saw marker  = {containerSeen}");
            Console.WriteLine($"[DIAG] container type           = {containerType}");
            Console.WriteLine($"[DIAG] probeStore count         = {probeStoreCount}");
            Console.WriteLine($"[DIAG] probeStore has marker    = {probeStoreHasMarker}");
            Console.WriteLine($"[DIAG] probeStore reporters     = {probeStoreReporters}");
            if (probeStoreError != null) Console.WriteLine($"[DIAG] probeStore error         = {probeStoreError}");
            Console.WriteLine($"[DIAG] HasObservers (pre-sub)   = {subscriberCount}");
            Console.WriteLine($"[DIAG] entries returned         = {(lastBody?.Length ?? 0)} chars");
            Console.WriteLine($"[DIAG] marker text              = {marker}");

            lastBody.Should().Contain(marker,
                because: "ReportStatus.Log.OnInformation must reach the InMemoryAppStatusStore via LocalAppStatusManager within 30s. " +
                         $"directSub saw marker={directSeen}; HasObservers(pre-sub)={subscriberCount}");
        }

        private static int FindFreePort()
        {
            var l = new TcpListener(System.Net.IPAddress.Loopback, 0);
            l.Start();
            var port = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }
    }
}
