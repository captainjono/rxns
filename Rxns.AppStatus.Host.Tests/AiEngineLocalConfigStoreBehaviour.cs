using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using Rxns.AppStatus.Host.Ai;

namespace Rxns.AppStatus.Host.Tests
{
    /// <summary>
    /// Round-trip behaviour for <see cref="AiEngineLocalConfigStore"/>. Caught a
    /// real regression where engines added via the UI persisted to disk but
    /// never loaded back after restart — the read path returned null when no
    /// base <c>appstatus.config</c> existed, while the write path fell back
    /// to <see cref="AppContext.BaseDirectory"/>. These tests pin the
    /// resolution chain so both sides always agree.
    /// </summary>
    [TestClass]
    [TestCategory("EngineConfigStore")]
    public class AiEngineLocalConfigStoreBehaviour
    {
        private string _tempDir;
        private string _previousEnv;

        [TestInitialize]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "rxns-ai-store-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            // RXNS_APPSTATUS_CONFIG_DIR takes precedence over every other
            // probe in ResolvePath, so we can scope the whole store to a
            // throwaway directory without polluting the test bin dir.
            _previousEnv = Environment.GetEnvironmentVariable("RXNS_APPSTATUS_CONFIG_DIR");
            Environment.SetEnvironmentVariable("RXNS_APPSTATUS_CONFIG_DIR", _tempDir);
        }

        [TestCleanup]
        public void TearDown()
        {
            Environment.SetEnvironmentVariable("RXNS_APPSTATUS_CONFIG_DIR", _previousEnv);
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* ignore cleanup races */ }
        }

        [TestMethod]
        public void load_returns_empty_list_when_no_file_exists()
        {
            new AiEngineLocalConfigStore().Load().Should().BeEmpty();
        }

        [TestMethod]
        public void upsert_then_load_round_trips_a_single_engine()
        {
            var store = new AiEngineLocalConfigStore();
            var cfg = new AiEngineCfg
            {
                Id = "ollama-laptop",
                Kind = "ollama",
                Label = "Ollama · laptop",
                Endpoint = "http://localhost:11434",
                Model = "qwen2.5-coder:7b"
            };

            store.Upsert(cfg);

            // Confirm the file landed inside the configured dir (no surprises
            // about which directory the writer chose).
            var expected = Path.Combine(_tempDir, "appstatus.local.config");
            File.Exists(expected).Should().BeTrue(because: "writer must land the file inside RXNS_APPSTATUS_CONFIG_DIR");

            // Fresh store instance — simulates a portal restart re-loading
            // engines from disk.
            var loaded = new AiEngineLocalConfigStore().Load();
            loaded.Should().HaveCount(1);
            loaded[0].Id.Should().Be("ollama-laptop");
            loaded[0].Kind.Should().Be("ollama");
            loaded[0].Endpoint.Should().Be("http://localhost:11434");
            loaded[0].Model.Should().Be("qwen2.5-coder:7b");
        }

        [TestMethod]
        public void upsert_replaces_existing_entry_by_id_not_appends()
        {
            var store = new AiEngineLocalConfigStore();
            store.Upsert(new AiEngineCfg { Id = "x", Kind = "ollama", Endpoint = "http://a", Model = "m1" });
            store.Upsert(new AiEngineCfg { Id = "x", Kind = "ollama", Endpoint = "http://b", Model = "m2" });

            var loaded = new AiEngineLocalConfigStore().Load();
            loaded.Should().HaveCount(1, because: "second Upsert with same Id must REPLACE not append");
            loaded[0].Endpoint.Should().Be("http://b");
            loaded[0].Model.Should().Be("m2");
        }

        [TestMethod]
        public void remove_drops_the_entry_and_persists()
        {
            var store = new AiEngineLocalConfigStore();
            store.Upsert(new AiEngineCfg { Id = "a", Kind = "ollama", Model = "m" });
            store.Upsert(new AiEngineCfg { Id = "b", Kind = "claude", Model = "m" });

            store.Remove("a");

            var loaded = new AiEngineLocalConfigStore().Load();
            loaded.Should().HaveCount(1);
            loaded[0].Id.Should().Be("b");
        }

        [TestMethod]
        public void project_context_round_trips()
        {
            var store = new AiEngineLocalConfigStore();
            store.LoadProjectContext().Should().BeNull(because: "no file written yet");

            const string md =
                "# myapp support\n\n" +
                "Start: `powershell .\\build.ps1 -Serve`\n" +
                "Logs:  `C:\\src\\myapp\\SupportPortal\\bin\\Debug\\net10.0\\myapp-portal.log`";
            store.SaveProjectContext(md);

            new AiEngineLocalConfigStore().LoadProjectContext()
                .Should().Be(md, because: "ProjectContext must survive a fresh-store re-read (= restart)");
        }

        [TestMethod]
        public void project_context_and_engines_coexist_in_one_file()
        {
            // Make sure persisting one doesn't clobber the other.
            var store = new AiEngineLocalConfigStore();
            store.Upsert(new AiEngineCfg { Id = "ollama-x", Kind = "ollama", Model = "qwen2.5" });
            store.SaveProjectContext("hello world");

            var reload = new AiEngineLocalConfigStore();
            reload.Load().Should().HaveCount(1);
            reload.LoadProjectContext().Should().Be("hello world");

            // Now mutate engines and re-verify context still there.
            store.Upsert(new AiEngineCfg { Id = "claude-y", Kind = "claude", Model = "sonnet" });
            new AiEngineLocalConfigStore().LoadProjectContext().Should().Be("hello world",
                because: "engine upsert must not blow away ProjectContext (same file)");
        }

        [TestMethod]
        public void saving_empty_string_clears_project_context()
        {
            var store = new AiEngineLocalConfigStore();
            store.SaveProjectContext("something");
            new AiEngineLocalConfigStore().LoadProjectContext().Should().Be("something");

            store.SaveProjectContext("");
            new AiEngineLocalConfigStore().LoadProjectContext()
                .Should().BeNull(because: "empty save should clear the field, not write an empty string");
        }

        [TestMethod]
        public void upsert_then_load_works_without_a_base_appstatus_config_present()
        {
            // The earlier bug: when no base appstatus.config existed in any
            // probed directory, the read path returned null while the write
            // path silently fell back to BaseDirectory. The user's symptom was
            // "engines I added via the UI disappear after a portal restart".
            // We're already in a clean _tempDir with no base file — this test
            // exists specifically to lock that asymmetry shut.
            var baseFile = Path.Combine(_tempDir, "appstatus.config");
            File.Exists(baseFile).Should().BeFalse(because: "test setup: no base config in the configured dir");

            new AiEngineLocalConfigStore().Upsert(new AiEngineCfg
            {
                Id = "foundry-local",
                Kind = "foundry",
                Endpoint = "http://127.0.0.1:54997",
                Model = "qwen2.5-7b-instruct-qnn-npu:2"
            });

            var loaded = new AiEngineLocalConfigStore().Load();
            loaded.Should().HaveCount(1, because:
                "fresh store with no base appstatus.config must still load what the previous Upsert just wrote");
            loaded[0].Id.Should().Be("foundry-local");
        }
    }
}
