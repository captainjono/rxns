using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Rxns.AppStatus.Host.Ai;

namespace Rxns.AppStatus.Host.Tests
{
    /// <summary>
    /// Focused, network-free behaviour for <see cref="AiEngineScanner"/>. We boot
    /// a tiny <see cref="HttpListener"/> on a free loopback port that serves the
    /// OpenAI <c>/v1/models</c> shape exactly the way Ollama / Foundry do, then
    /// point the scanner at it. If these tests fail, the problem is in the
    /// scanner itself — not in DI wiring, not in the controller, not in the
    /// running Ollama on the dev machine.
    ///
    /// Added after a real-world miss: scanning 127.0.0.1 returned 0 hits even
    /// though Ollama was up. Lesson: probe ALL code paths via a deterministic
    /// fixture; "manual probe with Invoke-WebRequest works" doesn't imply
    /// "the in-process HttpClient inside the host works".
    /// </summary>
    [TestClass]
    [TestCategory("Scanner")]
    public class AiEngineScannerBehaviour
    {
        private HttpListener _listener;
        private int _port;
        private CancellationTokenSource _cts;
        private Task _loop;

        [TestInitialize]
        public void StartFakeAi()
        {
            _port = FindFreePort();
            _listener = new HttpListener();
            // HttpListener requires a strong-bound prefix; "+" or "*" need admin.
            // Use the loopback literal so we don't need elevation.
            _listener.Prefixes.Add("http://127.0.0.1:" + _port + "/");
            _listener.Start();
            _cts = new CancellationTokenSource();
            _loop = Task.Run(() => RunListener(_cts.Token));
        }

        [TestCleanup]
        public void StopFakeAi()
        {
            try { _cts.Cancel(); } catch { }
            try { _listener.Stop(); _listener.Close(); } catch { }
        }

        private async Task RunListener(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                HttpListenerContext c;
                try { c = await _listener.GetContextAsync().ConfigureAwait(false); }
                catch { return; }

                var path = c.Request.Url?.AbsolutePath ?? "";
                byte[] body;
                if (path == "/v1/models")
                {
                    body = Encoding.UTF8.GetBytes(
                        "{ \"object\": \"list\", \"data\": [" +
                        "{\"id\":\"qwen2.5-coder:7b\",\"object\":\"model\"}," +
                        "{\"id\":\"llama3.2:3b\",\"object\":\"model\"}" +
                        "] }");
                    c.Response.StatusCode = 200;
                    c.Response.ContentType = "application/json";
                }
                else
                {
                    body = Encoding.UTF8.GetBytes("{\"error\":\"not found\"}");
                    c.Response.StatusCode = 404;
                }

                c.Response.OutputStream.Write(body, 0, body.Length);
                c.Response.OutputStream.Close();
            }
        }

        [TestMethod]
        [Timeout(15 * 1000)]
        public async Task probe_finds_a_v1_models_responder()
        {
            var scanner = new AiEngineScanner();
            var result = await scanner.ProbeAsync("127.0.0.1", _port);

            result.Should().NotBeNull(because:
                "the fake server at 127.0.0.1:" + _port + " is responding with the OpenAI /v1/models JSON shape. " +
                "If this is null, the scanner has a bug in its HTTP probe (timeout too aggressive, " +
                "wrong URL, swallowed exception, or HttpClient misconfiguration).");

            result.Url.Should().Be("http://127.0.0.1:" + _port);
            result.Models.Should().Contain("qwen2.5-coder:7b")
                .And.Contain("llama3.2:3b");
        }

        [TestMethod]
        [Timeout(15 * 1000)]
        public async Task probe_returns_null_for_a_dead_port()
        {
            // Pick a port nothing's bound to (FindFreePort returns one, then we
            // don't open a listener) — the probe must return null, not block.
            var deadPort = FindFreePort();
            var scanner = new AiEngineScanner();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = await scanner.ProbeAsync("127.0.0.1", deadPort);
            sw.Stop();

            result.Should().BeNull(because: "no server is bound on " + deadPort);
            sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(7),
                because: "the probe must fail fast on connection refused — 5s timeout + overhead, not block");
        }

        [TestMethod]
        [Timeout(15 * 1000)]
        public async Task scan_a_single_host_finds_the_fake_server_when_port_is_in_the_list()
        {
            var scanner = new AiEngineScanner();
            var results = await scanner.ScanAsync("127.0.0.1", new[] { _port });

            results.Should().HaveCount(1);
            results[0].Url.Should().Be("http://127.0.0.1:" + _port);
            results[0].Models.Should().NotBeEmpty();
        }

        [TestMethod]
        [Timeout(15 * 1000)]
        public async Task scan_returns_empty_when_ports_dont_match()
        {
            // Pick a port the OS confirms is unused, then immediately release it
            // and tell the scanner to look there. Avoids the previous test bug
            // where we scanned default ports (11434/5273) — on a dev box with
            // Ollama actually running, that gave a false-positive hit and looked
            // like the scanner was broken when it was working correctly.
            var unusedPort = FindFreePort();
            var scanner = new AiEngineScanner();
            var results = await scanner.ScanAsync("127.0.0.1", new[] { unusedPort });

            results.Should().BeEmpty(because:
                "nothing is bound on port " + unusedPort + " — confirmed by FindFreePort just before the scan");
        }

        [TestMethod]
        public void expand_cidr_for_bare_ip_yields_single_address()
        {
            AiEngineScanner.ExpandCidr("127.0.0.1").ToList().Should().Equal(new[] { "127.0.0.1" });
        }

        [TestMethod]
        public void expand_cidr_for_slash32_yields_single_address()
        {
            AiEngineScanner.ExpandCidr("127.0.0.1/32").ToList().Should().Equal(new[] { "127.0.0.1" });
        }

        [TestMethod]
        public void expand_cidr_for_slash30_yields_two_host_addresses()
        {
            // /30 = 4 addresses, minus network + broadcast = 2 hosts.
            AiEngineScanner.ExpandCidr("10.0.0.0/30").ToList()
                .Should().Equal(new[] { "10.0.0.1", "10.0.0.2" });
        }

        [TestMethod]
        public void expand_cidr_rejects_prefixes_outside_16_to_32()
        {
            FluentActions.Invoking(() => AiEngineScanner.ExpandCidr("10.0.0.0/8").ToList())
                .Should().Throw<ArgumentException>(because: "/8 is too broad — the scanner caps at /16");
        }

        private static int FindFreePort()
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            var port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }
    }
}
