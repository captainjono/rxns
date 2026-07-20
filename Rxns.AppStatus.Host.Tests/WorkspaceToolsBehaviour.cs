using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Threading.Tasks;
using Rxns.AppStatus.Host.Ai;
using Rxns.AppStatus.Host.Ai.Tools;
using Rxns.AppStatus.Host.Ai.Workspace;

namespace Rxns.AppStatus.Host.Tests
{
    /// <summary>
    /// Pins the security envelope on the workspace JIT tools — read/list/search
    /// MUST reject any path that escapes the configured roots. Earlier without
    /// these tests it would have been trivial to ship a tool that lets the
    /// model exfiltrate arbitrary files (C:\Windows\... etc).
    /// </summary>
    [TestClass]
    [TestCategory("WorkspaceTools")]
    public class WorkspaceToolsBehaviour
    {
        private string _root;
        private AiOptions _options;

        [TestInitialize]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "rxns-ws-tools-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            File.WriteAllText(Path.Combine(_root, "README.md"), "# project\nstart with foo");
            Directory.CreateDirectory(Path.Combine(_root, "docs"));
            File.WriteAllText(Path.Combine(_root, "docs", "runbook.md"), "## runbook\nthis is the runbook\nthing two");
            File.WriteAllText(Path.Combine(_root, "src.cs"), "class Foo { void Bar() { } }");

            _options = new AiOptions { WorkspaceRoots = new System.Collections.Generic.List<string> { _root } };
        }

        [TestCleanup]
        public void TearDown()
        {
            try { Directory.Delete(_root, recursive: true); } catch { }
        }

        // ── PathGuard ───────────────────────────────────────────────────────

        [TestMethod]
        public void path_guard_accepts_paths_under_a_root()
        {
            var ok = WorkspacePathGuard.ResolveOrThrow(Path.Combine(_root, "README.md"), new[] { _root });
            ok.Should().EndWith("README.md");
        }

        [TestMethod]
        public void path_guard_rejects_paths_outside_every_root()
        {
            var outside = Path.GetTempPath();
            FluentActions.Invoking(() => WorkspacePathGuard.ResolveOrThrow(outside, new[] { _root }))
                .Should().Throw<UnauthorizedAccessException>();
        }

        [TestMethod]
        public void path_guard_rejects_dotdot_escape_attempt()
        {
            // A naive "starts-with" check would let "<root>/../sister" through
            // because the literal string starts with the root prefix. The
            // guard normalises via Path.GetFullPath first to defeat this.
            var sneaky = Path.Combine(_root, "..", "definitely-outside.txt");
            FluentActions.Invoking(() => WorkspacePathGuard.ResolveOrThrow(sneaky, new[] { _root }))
                .Should().Throw<UnauthorizedAccessException>();
        }

        [TestMethod]
        public void path_guard_rejects_prefix_collision_root_xx()
        {
            // Sibling directory whose name shares a prefix with the root
            // (e.g. root="C:/jan/rxns", attempted="C:/jan/rxnsXX"). The
            // separator-terminated comparison in the guard prevents this.
            var sister = _root + "XX";
            try
            {
                Directory.CreateDirectory(sister);
                File.WriteAllText(Path.Combine(sister, "leak.txt"), "should not leak");
                FluentActions.Invoking(() => WorkspacePathGuard.ResolveOrThrow(Path.Combine(sister, "leak.txt"), new[] { _root }))
                    .Should().Throw<UnauthorizedAccessException>();
            }
            finally
            {
                try { Directory.Delete(sister, recursive: true); } catch { }
            }
        }

        // ── ReadFileTool ────────────────────────────────────────────────────

        [TestMethod]
        public async Task read_file_returns_content_for_path_under_root()
        {
            var tool = new WorkspaceReadFileTool(_options);
            var r = await tool.ExecuteAsync(JObject.FromObject(new { path = Path.Combine(_root, "README.md") }).ToString());
            r.IsError.Should().BeFalse();
            r.OutputJson.Should().Contain("# project").And.Contain("start with foo");
        }

        [TestMethod]
        public async Task read_file_rejects_path_outside_root()
        {
            var tool = new WorkspaceReadFileTool(_options);
            var r = await tool.ExecuteAsync(JObject.FromObject(new { path = "C:/Windows/win.ini" }).ToString());
            r.IsError.Should().BeTrue(because: "everything outside the configured roots must be rejected");
        }

        [TestMethod]
        public async Task read_file_truncates_over_cap()
        {
            var big = Path.Combine(_root, "big.txt");
            File.WriteAllText(big, new string('x', WorkspaceReadFileTool.MaxBytes + 1024));
            var tool = new WorkspaceReadFileTool(_options);
            var r = await tool.ExecuteAsync(JObject.FromObject(new { path = big }).ToString());
            r.IsError.Should().BeFalse();
            var d = JObject.Parse(r.OutputJson);
            ((bool)d["truncated"]).Should().BeTrue();
            ((int)d["truncatedAfterBytes"]).Should().Be(WorkspaceReadFileTool.MaxBytes);
        }

        // ── ListFilesTool ───────────────────────────────────────────────────

        [TestMethod]
        public async Task list_files_default_pattern_finds_everything()
        {
            var tool = new WorkspaceListFilesTool(_options);
            var r = await tool.ExecuteAsync("{}");
            r.IsError.Should().BeFalse();
            var d = JObject.Parse(r.OutputJson);
            ((int)d["count"]).Should().BeGreaterThanOrEqualTo(3);
            r.OutputJson.Should().Contain("README.md").And.Contain("runbook.md").And.Contain("src.cs");
        }

        [TestMethod]
        public async Task list_files_with_pattern_scopes_to_md()
        {
            var tool = new WorkspaceListFilesTool(_options);
            var r = await tool.ExecuteAsync(JObject.FromObject(new { pattern = "**/*.md" }).ToString());
            var d = JObject.Parse(r.OutputJson);
            ((int)d["count"]).Should().Be(2, because: "README.md + docs/runbook.md");
            r.OutputJson.Should().NotContain("src.cs");
        }

        [TestMethod]
        public async Task list_files_rejects_root_outside_configured()
        {
            var tool = new WorkspaceListFilesTool(_options);
            var r = await tool.ExecuteAsync(JObject.FromObject(new { root = "C:/Windows" }).ToString());
            r.IsError.Should().BeTrue();
        }

        // ── SearchTool ──────────────────────────────────────────────────────

        [TestMethod]
        public async Task search_finds_literal_text_with_line_numbers()
        {
            var tool = new WorkspaceSearchTool(_options);
            var r = await tool.ExecuteAsync(JObject.FromObject(new { query = "runbook" }).ToString());
            r.IsError.Should().BeFalse();
            var d = JObject.Parse(r.OutputJson);
            ((int)d["count"]).Should().BeGreaterThanOrEqualTo(1);
            r.OutputJson.Should().Contain("runbook.md");
            r.OutputJson.Should().MatchRegex("\"line\":\\s*[0-9]+");
        }

        [TestMethod]
        public async Task search_regex_mode_compiles_query_as_regex()
        {
            var tool = new WorkspaceSearchTool(_options);
            var r = await tool.ExecuteAsync(JObject.FromObject(new { query = "class\\s+\\w+", regex = true, pattern = "**/*.cs" }).ToString());
            r.IsError.Should().BeFalse();
            r.OutputJson.Should().Contain("class Foo");
        }

        [TestMethod]
        public async Task search_with_bad_regex_returns_clean_error()
        {
            var tool = new WorkspaceSearchTool(_options);
            var r = await tool.ExecuteAsync(JObject.FromObject(new { query = "[unclosed", regex = true }).ToString());
            r.IsError.Should().BeTrue();
            r.ErrorMessage.Should().Contain("bad regex");
        }

        [TestMethod]
        public async Task search_rejects_root_outside_configured()
        {
            var tool = new WorkspaceSearchTool(_options);
            var r = await tool.ExecuteAsync(JObject.FromObject(new { query = "anything", root = Path.GetTempPath() }).ToString());
            r.IsError.Should().BeTrue();
        }
    }
}
