using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Rxns.Ai;
using Rxns.AppStatus.Host.Ai.Workspace;

namespace Rxns.AppStatus.Host.Tests
{
    /// <summary>
    /// Chunker + indexer + cosine-search behaviour, fully offline via a fake
    /// embeddings engine. The fake produces deterministic vectors based on
    /// token-overlap with the query, which lets us verify that the search
    /// surface actually ranks chunks by similarity rather than by accident.
    /// </summary>
    [TestClass]
    [TestCategory("WorkspaceIndexer")]
    public class WorkspaceIndexerBehaviour
    {
        private string _root;

        [TestInitialize]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "rxns-idx-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TestCleanup]
        public void TearDown()
        {
            try { Directory.Delete(_root, recursive: true); } catch { }
        }

        // ── Chunker ─────────────────────────────────────────────────────────

        [TestMethod]
        public void chunker_splits_short_file_into_one_chunk()
        {
            File.WriteAllText(Path.Combine(_root, "tiny.md"), "# tiny\nhello world");
            var chunks = new WorkspaceChunker().ChunkFile(_root, Path.Combine(_root, "tiny.md"));
            chunks.Should().HaveCount(1);
            chunks[0].Text.Should().Contain("hello world");
            chunks[0].LineStart.Should().Be(1);
        }

        [TestMethod]
        public void chunker_splits_long_file_at_blank_lines_when_past_threshold()
        {
            // Build a file long enough to force multiple chunks: 5 paragraphs
            // of ~600 chars each.
            var paragraphs = Enumerable.Range(0, 5)
                .Select(i => "## section " + i + "\n" + new string('x', 580))
                .ToList();
            var content = string.Join("\n\n", paragraphs);
            File.WriteAllText(Path.Combine(_root, "long.md"), content);

            var chunks = new WorkspaceChunker { TargetChars = 1200 }
                .ChunkFile(_root, Path.Combine(_root, "long.md"));
            chunks.Should().HaveCountGreaterThan(1);
            // Line ranges must be monotonic and non-overlapping (one chunk
            // ends before the next begins).
            for (var i = 1; i < chunks.Count; i++)
                chunks[i].LineStart.Should().BeGreaterThan(chunks[i - 1].LineEnd);
        }

        // ── Indexer + Search (with fake embeddings) ─────────────────────────

        // Deterministic, content-aware fake: each output dimension corresponds
        // to a word's presence (0 or 1). Cosine similarity then reflects word
        // overlap — good enough to verify the search surface.
        private class FakeEmbeddings : IAiEmbeddingsEngine
        {
            public string EngineId => "fake";
            public string Label    => "fake";
            public string Kind     => "fake";
            public string ModelId  => "fake-model";
            public bool   IsAvailable => true;

            private static readonly string[] Vocab = new[]
            {
                "restart","portal","powershell","logs","monitor","start","deploy",
                "rxns","delivery","insights","build","test","appinsights","engine"
            };

            public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
            {
                IReadOnlyList<float[]> result = texts.Select(t =>
                {
                    var lower = (t ?? "").ToLowerInvariant();
                    var v = new float[Vocab.Length];
                    for (var i = 0; i < Vocab.Length; i++)
                        v[i] = lower.Contains(Vocab[i]) ? 1f : 0f;
                    return v;
                }).ToList();
                return Task.FromResult(result);
            }
        }

        [TestMethod]
        public async Task build_then_search_finds_the_right_chunk_via_cosine()
        {
            File.WriteAllText(Path.Combine(_root, "ops.md"),
                "## restart the portal\n" +
                "Run powershell ./SupportPortal/start-portal.ps1 -Restart -Background -Force.\n" +
                "\n" +
                "## build commands\n" +
                "Use rxns-build.ps1 for the framework.");
            File.WriteAllText(Path.Combine(_root, "deploy.md"),
                "## service deploy\n" +
                "Deploy the service via the deployment tool.");

            var indexer = new WorkspaceIndexer(new WorkspaceChunker { TargetChars = 600 });
            var idx = await indexer.BuildAsync(_root, new[]
            {
                Path.Combine(_root, "ops.md"),
                Path.Combine(_root, "deploy.md")
            }, new FakeEmbeddings());

            idx.Chunks.Should().NotBeEmpty();
            idx.EngineId.Should().Be("fake");

            // Query about restarting → top hit should be the ops.md "restart the portal" chunk.
            var q = (await new FakeEmbeddings().EmbedAsync(new[] { "how do I restart the portal" }))[0];
            var hits = idx.Search(q, topK: 3);
            hits.Should().NotBeEmpty();
            hits[0].Chunk.Text.Should().Contain("restart the portal",
                because: "the cosine-best chunk for a 'restart portal' query is the one literally about restarting the portal");

            // Query about service deploy → top hit should be deploy.md.
            var q2 = (await new FakeEmbeddings().EmbedAsync(new[] { "deploy the service" }))[0];
            var hits2 = idx.Search(q2, topK: 3);
            hits2.Should().NotBeEmpty();
            hits2[0].Chunk.RelativePath.Should().Contain("deploy.md");
        }

        [TestMethod]
        public async Task index_round_trips_via_disk()
        {
            File.WriteAllText(Path.Combine(_root, "one.md"), "# one\nstart with powershell");
            var indexer = new WorkspaceIndexer(new WorkspaceChunker { TargetChars = 600 });
            var built = await indexer.BuildAsync(_root, new[] { Path.Combine(_root, "one.md") }, new FakeEmbeddings());
            built.Chunks.Should().HaveCountGreaterThan(0);

            // Fresh indexer = simulated restart. Loads from disk.
            var fresh = new WorkspaceIndexer(new WorkspaceChunker());
            var loaded = fresh.Load(_root);
            loaded.Should().NotBeNull();
            loaded.Chunks.Should().HaveCount(built.Chunks.Count);
            loaded.EngineId.Should().Be("fake");

            // Status reflects built=true with chunk count + non-zero size.
            var status = fresh.Status(_root);
            status.Built.Should().BeTrue();
            status.ChunkCount.Should().Be(built.Chunks.Count);
            status.SizeBytes.Should().BeGreaterThan(0);
        }

        [TestMethod]
        public async Task clear_removes_the_index_from_disk_and_status_reflects_it()
        {
            File.WriteAllText(Path.Combine(_root, "one.md"), "# one\nstart with powershell");
            var indexer = new WorkspaceIndexer(new WorkspaceChunker());
            await indexer.BuildAsync(_root, new[] { Path.Combine(_root, "one.md") }, new FakeEmbeddings());
            indexer.Status(_root).Built.Should().BeTrue();

            indexer.Clear(_root);
            var fresh = new WorkspaceIndexer(new WorkspaceChunker());
            fresh.Status(_root).Built.Should().BeFalse(because:
                "Clear() must delete the index file so a fresh indexer can't load it from disk");
        }

        [TestMethod]
        public async Task build_throws_when_engine_unavailable()
        {
            File.WriteAllText(Path.Combine(_root, "one.md"), "# one\nhello");
            var indexer = new WorkspaceIndexer(new WorkspaceChunker());
            var unavailable = new UnavailableEmbeddings();
            await FluentActions.Invoking(async () =>
                await indexer.BuildAsync(_root, new[] { Path.Combine(_root, "one.md") }, unavailable))
                .Should().ThrowAsync<InvalidOperationException>();
        }

        private class UnavailableEmbeddings : IAiEmbeddingsEngine
        {
            public string EngineId => "x";
            public string Label    => "x";
            public string Kind     => "x";
            public string ModelId  => "x";
            public bool   IsAvailable => false;
            public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
                => throw new InvalidOperationException("should never be called");
        }
    }
}
