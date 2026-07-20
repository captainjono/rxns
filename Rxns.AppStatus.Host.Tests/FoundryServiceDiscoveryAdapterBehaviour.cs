using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Rxns.AppStatus.Host.Ai;
using Rxns.AppStatus.Host.Ai.Discovery;

namespace Rxns.AppStatus.Host.Tests
{
    /// <summary>
    /// Behaviour for <see cref="FoundryServiceDiscoveryAdapter"/> — verifies
    /// the CLI-output parser handles the real-world shape (emoji prefix,
    /// trailing punctuation, "service not running" empty case, missing CLI
    /// case) without needing a real Foundry install in the test environment.
    /// </summary>
    [TestClass]
    [TestCategory("Discovery")]
    public class FoundryServiceDiscoveryAdapterBehaviour
    {
        private class FakeRunner : ICommandRunner
        {
            // Single-command default (used by the existing "service status only"
            // tests). When set, applies to every invocation regardless of args.
            public string Stdout { get; set; } = "";
            public string Stderr { get; set; } = "";
            public int ExitCode { get; set; } = 0;
            public bool ExecutableMissing { get; set; } = false;

            // Per-subcommand canned output. Tests that exercise the merge of
            // 'service status' + 'model list' set both keys; the dispatcher
            // matches on the leading token of the `arguments` string ("service",
            // "model", …). Falls back to Stdout/Stderr/ExitCode when no key
            // matches so the simple tests stay terse.
            public Dictionary<string, CommandResult> ByArgs { get; } =
                new Dictionary<string, CommandResult>(System.StringComparer.OrdinalIgnoreCase);

            public Task<CommandResult> RunAsync(string executable, string arguments, int timeoutMs = 5000, CancellationToken ct = default)
            {
                if (ByArgs.Count > 0 && !string.IsNullOrWhiteSpace(arguments))
                {
                    foreach (var kv in ByArgs)
                        if (arguments.StartsWith(kv.Key, System.StringComparison.OrdinalIgnoreCase))
                            return Task.FromResult(kv.Value);
                }
                return Task.FromResult(new CommandResult { Stdout = Stdout, Stderr = Stderr, ExitCode = ExitCode, ExecutableMissing = ExecutableMissing });
            }
        }

        [TestMethod]
        public void parse_endpoint_handles_real_foundry_output_with_emoji_and_trailing_punct()
        {
            const string sample = "🟢 Service is Started on http://127.0.0.1:54997/, PID 1968!";
            FoundryServiceDiscoveryAdapter.ParseEndpoint(sample)
                .Should().Be("http://127.0.0.1:54997",
                    because: "we strip the trailing '/' and ', PID …' but keep host:port intact");
        }

        [TestMethod]
        public void parse_endpoint_returns_null_when_no_url_is_present()
        {
            FoundryServiceDiscoveryAdapter.ParseEndpoint("Service is not running.")
                .Should().BeNull();
            FoundryServiceDiscoveryAdapter.ParseEndpoint("")
                .Should().BeNull();
            FoundryServiceDiscoveryAdapter.ParseEndpoint(null)
                .Should().BeNull();
        }

        [TestMethod]
        public void parse_endpoint_picks_first_url_when_multiple_are_in_output()
        {
            const string twoLines =
                "Service is Started on http://127.0.0.1:54997/, PID 1968!\n" +
                "Diagnostics: http://127.0.0.1:54998/health";
            FoundryServiceDiscoveryAdapter.ParseEndpoint(twoLines)
                .Should().Be("http://127.0.0.1:54997");
        }

        [TestMethod]
        public async Task discover_returns_empty_when_cli_is_missing()
        {
            var adapter = new FoundryServiceDiscoveryAdapter(
                new FakeRunner { ExecutableMissing = true },
                new AiEngineScanner());

            var results = await adapter.DiscoverAsync();
            results.Should().BeEmpty(because: "no foundry CLI on PATH → opportunistic skip, not throw");
        }

        [TestMethod]
        public async Task discover_returns_empty_when_cli_output_has_no_url()
        {
            var adapter = new FoundryServiceDiscoveryAdapter(
                new FakeRunner { Stdout = "Service is not running." },
                new AiEngineScanner());

            var results = await adapter.DiscoverAsync();
            results.Should().BeEmpty();
        }

        [TestMethod]
        public void parse_installed_models_picks_real_foundry_ids_from_table_output()
        {
            // Real-shape stdout from `foundry model list` (table-style, header
            // row + a few model rows). The user's bug: Phi was downloaded but
            // not loaded, so /v1/models hid it from the picker. This test pins
            // that Phi-shaped + qwen-shaped IDs come out of the parser.
            const string sample =
                "Alias                                    Device   Task                Filesize\n" +
                "phi-4-mini-instruct-qnn-npu              NPU      chat-completion     2.3GB\n" +
                "Phi-4-mini-instruct-qnn-npu:2            NPU      chat-completion     2.3GB\n" +
                "qwen2.5-7b-instruct-qnn-npu:2            NPU      chat-completion     4.0GB\n" +
                "deepseek-r1-distill-qwen-7b-cpu:1        CPU      chat-completion     4.5GB\n";

            var ids = FoundryServiceDiscoveryAdapter.ParseInstalledModels(sample);

            ids.Should().Contain("phi-4-mini-instruct-qnn-npu");
            ids.Should().Contain("Phi-4-mini-instruct-qnn-npu:2");
            ids.Should().Contain("qwen2.5-7b-instruct-qnn-npu:2");
            ids.Should().Contain("deepseek-r1-distill-qwen-7b-cpu:1");
            // Table-header noise must not leak through.
            ids.Should().NotContain("Alias");
            ids.Should().NotContain("Device");
            ids.Should().NotContain("Filesize");
        }

        [TestMethod]
        public void parse_installed_models_strips_ansi_escapes_so_colourised_output_doesnt_break_the_regex()
        {
            // ESC[32m turns text green; the parser must see through it.
            const string ansi = "\x1b[32mphi-4-mini-instruct-qnn-npu:2\x1b[0m";
            FoundryServiceDiscoveryAdapter.ParseInstalledModels(ansi)
                .Should().Contain("phi-4-mini-instruct-qnn-npu:2");
        }

        [TestMethod]
        public void parse_installed_models_returns_empty_on_null_or_whitespace()
        {
            FoundryServiceDiscoveryAdapter.ParseInstalledModels(null).Should().BeEmpty();
            FoundryServiceDiscoveryAdapter.ParseInstalledModels("").Should().BeEmpty();
            FoundryServiceDiscoveryAdapter.ParseInstalledModels("   \n  ").Should().BeEmpty();
        }

        [TestMethod]
        public async Task discover_merges_installed_models_when_v1_models_only_returns_loaded_set()
        {
            // The reported "Phi isn't in the picker" bug — Foundry's
            // /v1/models endpoint surfaces only the model that's currently
            // loaded, so even with Phi downloaded it's invisible to the picker.
            // The adapter must shell `foundry model list` and merge the
            // installed-but-unloaded models into the discovered entry.
            //
            // (We use a non-listening port for the probe so it fails fast and
            // we test the *fallback* path exclusively — the production path
            // would also union with /v1/models hits, which MergeUnique handles.)
            var runner = new FakeRunner();
            runner.ByArgs["service"] = new CommandResult
            {
                Stdout = "🟢 Service is Started on http://127.0.0.1:1/, PID 9999!",
                ExitCode = 0
            };
            // The adapter must shell `foundry cache ls` (what's downloaded),
            // NOT `foundry model list` (the full remote catalogue). If we use
            // the wrong command the picker fills with ~71 unrelated entries —
            // that was the regression we caught after the first round.
            runner.ByArgs["cache"] = new CommandResult
            {
                Stdout =
                    "Models cached on device:\n" +
                    "   Alias                                       Model ID\n" +
                    "💾 phi-4-mini                                   Phi-4-mini-instruct-qnn-npu:2\n" +
                    "💾 qwen2.5                                      qwen2.5-7b-instruct-qnn-npu:2\n",
                ExitCode = 0
            };
            // If we accidentally fall back to `model list`, fail loudly — that
            // command returns the entire remote catalogue and floods the picker.
            runner.ByArgs["model"] = new CommandResult
            {
                Stdout = "should-not-be-called-this-is-the-catalogue",
                ExitCode = 0
            };

            var adapter = new FoundryServiceDiscoveryAdapter(runner, new AiEngineScanner());
            var results = await adapter.DiscoverAsync();

            results.Should().HaveCount(1);
            results[0].Models.Should().Contain("Phi-4-mini-instruct-qnn-npu:2",
                because: "Phi is in the local cache; discovery must surface it even when /v1/models doesn't");
            results[0].Models.Should().Contain("qwen2.5-7b-instruct-qnn-npu:2");
            // Aliases ("phi-4-mini", "qwen2.5") that share the row are also fair
            // game — they're valid model IDs to address the model by — so we
            // don't assert against them. What we DO assert is that nothing from
            // the remote-catalogue stdout leaks through.
            results[0].Models.Should().NotContain("should-not-be-called-this-is-the-catalogue");
        }

        [TestMethod]
        public async Task list_installed_models_returns_empty_when_foundry_cli_is_missing()
        {
            var adapter = new FoundryServiceDiscoveryAdapter(
                new FakeRunner { ExecutableMissing = true },
                new AiEngineScanner());

            var ids = await adapter.ListInstalledModelsAsync();
            ids.Should().BeEmpty(because: "no foundry CLI on PATH → opportunistic skip");
        }

        [TestMethod]
        public async Task discover_returns_unprobed_entry_when_url_present_but_probe_fails()
        {
            // CLI says "Service is Started on http://127.0.0.1:1/", port 1 has
            // nothing listening → probe fails. The adapter still surfaces the URL
            // so the operator can adopt and retry once Foundry stabilises (the
            // "race between service-start and listener-bind" case).
            var adapter = new FoundryServiceDiscoveryAdapter(
                new FakeRunner { Stdout = "Service is Started on http://127.0.0.1:1/, PID 9999!" },
                new AiEngineScanner());

            var results = await adapter.DiscoverAsync();
            results.Should().HaveCount(1);
            results[0].Url.Should().Be("http://127.0.0.1:1");
            results[0].Kind.Should().Be("foundry");
            results[0].Models.Should().BeEmpty();
        }
    }
}
