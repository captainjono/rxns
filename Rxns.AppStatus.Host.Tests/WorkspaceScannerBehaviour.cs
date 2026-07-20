using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;
using Rxns.AppStatus.Host.Ai.Workspace;

namespace Rxns.AppStatus.Host.Tests
{
    /// <summary>
    /// Scanner walks each configured workspace root and matches the default
    /// (or operator-supplied) discovery globs. These tests stand up a small
    /// fixture filesystem under %TEMP% so the scanner has known content to
    /// match — no dependency on any real repo layout.
    /// </summary>
    [TestClass]
    [TestCategory("WorkspaceScanner")]
    public class WorkspaceScannerBehaviour
    {
        private string _root1;
        private string _root2;

        [TestInitialize]
        public void SetUp()
        {
            _root1 = Path.Combine(Path.GetTempPath(), "rxns-ws-scan-" + Guid.NewGuid().ToString("N"));
            _root2 = Path.Combine(Path.GetTempPath(), "rxns-ws-scan-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root1);
            Directory.CreateDirectory(_root2);
        }

        [TestCleanup]
        public void TearDown()
        {
            try { Directory.Delete(_root1, recursive: true); } catch { }
            try { Directory.Delete(_root2, recursive: true); } catch { }
        }

        private void WriteFile(string root, string relativePath, string content)
        {
            var abs = Path.Combine(root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(abs));
            File.WriteAllText(abs, content);
        }

        [TestMethod]
        public void scan_with_default_patterns_finds_readme_and_docs_markdown()
        {
            WriteFile(_root1, "README.md", "# root readme");
            WriteFile(_root1, "docs/architecture.md", "# arch");
            WriteFile(_root1, "docs/deeply/nested/runbook.md", "# nested");
            WriteFile(_root1, "node_modules/big.txt", "should not be matched (default patterns are md-only)");
            WriteFile(_root1, "src/Program.cs", "should be ignored");

            var hits = new WorkspaceScanner().Scan(new[] { _root1 });

            var rel = hits.Select(h => h.RelativePath).OrderBy(p => p).ToList();
            rel.Should().BeEquivalentTo(new[]
            {
                "README.md",
                "docs/architecture.md",
                "docs/deeply/nested/runbook.md"
            });
            hits.Should().OnlyContain(h => h.Root == Path.GetFullPath(_root1));
        }

        [TestMethod]
        public void scan_returns_size_and_modified_time_per_file()
        {
            WriteFile(_root1, "README.md", "hello world");
            var hits = new WorkspaceScanner().Scan(new[] { _root1 });
            hits.Should().HaveCount(1);
            hits[0].SizeBytes.Should().Be(11);
            hits[0].ModifiedUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
            hits[0].AbsolutePath.Should().EndWith("README.md");
        }

        [TestMethod]
        public void scan_handles_multi_root_independently()
        {
            WriteFile(_root1, "README.md", "root one");
            WriteFile(_root2, "docs/runbook.md", "root two");

            var hits = new WorkspaceScanner().Scan(new[] { _root1, _root2 });

            hits.Should().HaveCount(2);
            hits.Select(h => h.Root).Distinct().Should().HaveCount(2);
            hits.Where(h => h.Root == Path.GetFullPath(_root1)).Should().HaveCount(1);
            hits.Where(h => h.Root == Path.GetFullPath(_root2)).Should().HaveCount(1);
        }

        [TestMethod]
        public void scan_with_custom_patterns_overrides_defaults()
        {
            WriteFile(_root1, "README.md", "default would match");
            WriteFile(_root1, "src/start-portal.ps1", "custom would match");
            WriteFile(_root1, "src/Program.cs", "ignored");

            var hits = new WorkspaceScanner().Scan(new[] { _root1 }, new[] { "**/start-*.ps1" });

            hits.Select(h => h.RelativePath).Should().BeEquivalentTo(new[] { "src/start-portal.ps1" });
        }

        [TestMethod]
        public void scan_excludes_via_bang_pattern()
        {
            WriteFile(_root1, "docs/keep.md", "keep");
            WriteFile(_root1, "docs/legacy/skip.md", "skip");

            var hits = new WorkspaceScanner().Scan(new[] { _root1 }, new[] { "docs/**/*.md", "!docs/legacy/**" });

            hits.Select(h => h.RelativePath).Should().BeEquivalentTo(new[] { "docs/keep.md" });
        }

        [TestMethod]
        public void scan_skips_missing_and_invalid_roots_without_throwing()
        {
            WriteFile(_root1, "README.md", "ok");
            var missing = Path.Combine(Path.GetTempPath(), "definitely-not-real-" + Guid.NewGuid().ToString("N"));

            var hits = new WorkspaceScanner().Scan(new[] { missing, _root1, "" , null });

            hits.Should().HaveCount(1);
            hits[0].RelativePath.Should().Be("README.md");
        }

        [TestMethod]
        public void build_knowledge_preamble_inlines_selected_files_with_delimiters()
        {
            WriteFile(_root1, "README.md", "# project\nstart-cmd: foo");
            WriteFile(_root1, "docs/runbook.md", "# runbook\nclick the thing");

            var selected = new[]
            {
                Path.GetFullPath(Path.Combine(_root1, "README.md")),
                Path.GetFullPath(Path.Combine(_root1, "docs", "runbook.md"))
            };

            var preamble = new WorkspaceScanner().BuildKnowledgePreamble(selected);

            preamble.Should().Contain("# project");
            preamble.Should().Contain("# runbook");
            preamble.Should().Contain("README.md");
            preamble.Should().Contain("runbook.md");
            // Each block must carry a delimiter so the model can tell them apart.
            (preamble.Split(new[] { "---" }, StringSplitOptions.None).Length - 1).Should().BeGreaterThan(1);
        }

        [TestMethod]
        public void build_knowledge_preamble_respects_byte_budget()
        {
            // 1000-byte file then a 1000-byte file. Budget of 1200 means the
            // second file's body should be skipped, name listed.
            WriteFile(_root1, "a.md", new string('a', 1000));
            WriteFile(_root1, "b.md", new string('b', 1000));

            var selected = new[]
            {
                Path.GetFullPath(Path.Combine(_root1, "a.md")),
                Path.GetFullPath(Path.Combine(_root1, "b.md"))
            };

            var preamble = new WorkspaceScanner().BuildKnowledgePreamble(selected, maxBytes: 1200);

            preamble.Should().Contain("aaaa", because: "first file's body must be present");
            preamble.Should().NotContain("bbbbbbbbbbbbbbbb",
                because: "second file's body must NOT be present once budget is exhausted");
            preamble.Should().Contain("skipped due to byte budget",
                because: "the truncation report tells the model what was dropped");
            preamble.Should().Contain("b.md");
        }
    }
}
