using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Rxns.Ai;
using Rxns.Logging;

namespace Rxns.AppStatus.Host.Ai.Workspace
{
    /// <summary>Status snapshot for a single workspace root's knowledge index.</summary>
    public class WorkspaceIndexStatus
    {
        public string   Root        { get; set; }
        public bool     Built       { get; set; }
        public int      ChunkCount  { get; set; }
        public int      Dimensions  { get; set; }
        public DateTime? BuiltAtUtc { get; set; }
        public string   EngineId    { get; set; }
        public string   Model       { get; set; }
        public string   IndexPath   { get; set; }
        public long     SizeBytes   { get; set; }
    }

    /// <summary>
    /// Orchestrates building, loading, clearing, and querying per-root
    /// knowledge indexes. Loads existing indexes lazily on first search;
    /// build is explicit (operator clicks "Train" in the UI).
    /// </summary>
    public class WorkspaceIndexer
    {
        private readonly WorkspaceChunker _chunker;
        private readonly object _lock = new object();
        private readonly Dictionary<string, WorkspaceKnowledgeIndex> _inMem = new Dictionary<string, WorkspaceKnowledgeIndex>(StringComparer.OrdinalIgnoreCase);

        public WorkspaceIndexer(WorkspaceChunker chunker)
        {
            _chunker = chunker ?? new WorkspaceChunker();
        }

        /// <summary>Build (or rebuild) the index for one workspace root using
        /// the supplied files + embeddings engine. Writes to disk on success.</summary>
        public async Task<WorkspaceKnowledgeIndex> BuildAsync(
            string root,
            IEnumerable<string> absoluteFilePaths,
            IAiEmbeddingsEngine engine,
            CancellationToken ct = default,
            Action<int, int, string> onProgress = null)
        {
            if (engine == null || !engine.IsAvailable)
                throw new InvalidOperationException("no embeddings engine available — pick one in Settings");
            var rootFull = Path.GetFullPath(root);
            var files = (absoluteFilePaths ?? Array.Empty<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToList();
            if (files.Count == 0)
                throw new InvalidOperationException("no files selected to index — tick at least one in Workspace settings");

            onProgress?.Invoke(0, files.Count, "chunking");
            var chunks = _chunker.ChunkFiles(rootFull, files);
            if (chunks.Count == 0)
                throw new InvalidOperationException("nothing to embed — selected files produced zero chunks (empty / unreadable?)");

            // Batch into reasonable sizes — both Ollama and Foundry accept big
            // batches but memory is finite and partial progress is useful.
            const int BatchSize = 32;
            var allEmbeddings = new List<float[]>(chunks.Count);
            for (var i = 0; i < chunks.Count; i += BatchSize)
            {
                ct.ThrowIfCancellationRequested();
                var slice = chunks.Skip(i).Take(BatchSize).Select(c => c.Text).ToList();
                onProgress?.Invoke(i, chunks.Count, "embedding " + (i + slice.Count) + "/" + chunks.Count);
                IReadOnlyList<float[]> vecs;
                try { vecs = await engine.EmbedAsync(slice, ct).ConfigureAwait(false); }
                catch (Exception ex)
                {
                    ("WorkspaceIndexer.BuildAsync: embed batch " + i + " failed: " + ex.Message).LogDebug("AiWorkspace");
                    throw;
                }
                allEmbeddings.AddRange(vecs);
            }

            var dims = allEmbeddings.FirstOrDefault(e => e != null && e.Length > 0)?.Length ?? 0;
            var index = new WorkspaceKnowledgeIndex
            {
                Root       = rootFull,
                EngineId   = engine.EngineId,
                Model      = engine.ModelId,
                Dimensions = dims,
                BuiltAtUtc = DateTime.UtcNow,
                SelectedFilesAtBuild = files,
                Chunks     = chunks.Select((c, i) => new WorkspaceKnowledgeIndex.IndexedChunk
                {
                    Path         = c.AbsolutePath,
                    RelativePath = c.RelativePath,
                    LineStart    = c.LineStart,
                    LineEnd      = c.LineEnd,
                    Text         = c.Text,
                    Embedding    = i < allEmbeddings.Count ? allEmbeddings[i] : Array.Empty<float>()
                }).ToList()
            };
            onProgress?.Invoke(chunks.Count, chunks.Count, "saving");
            index.Save();
            lock (_lock) _inMem[rootFull] = index;
            onProgress?.Invoke(chunks.Count, chunks.Count, "done");
            return index;
        }

        public WorkspaceKnowledgeIndex Load(string root)
        {
            var rootFull = Path.GetFullPath(root);
            lock (_lock)
            {
                if (_inMem.TryGetValue(rootFull, out var cached)) return cached;
            }
            var disk = WorkspaceKnowledgeIndex.Load(rootFull);
            if (disk != null) lock (_lock) _inMem[rootFull] = disk;
            return disk;
        }

        public void Clear(string root)
        {
            var rootFull = Path.GetFullPath(root);
            lock (_lock) _inMem.Remove(rootFull);
            WorkspaceKnowledgeIndex.Clear(rootFull);
        }

        public WorkspaceIndexStatus Status(string root)
        {
            var rootFull = Path.GetFullPath(root);
            var idx = Load(rootFull);
            var path = WorkspaceKnowledgeIndex.ResolveIndexPath(rootFull);
            long size = 0;
            if (path != null && File.Exists(path))
            {
                try { size = new FileInfo(path).Length; } catch { /* ignore */ }
            }
            return new WorkspaceIndexStatus
            {
                Root        = rootFull,
                Built       = idx != null && idx.Chunks != null && idx.Chunks.Count > 0,
                ChunkCount  = idx?.Chunks?.Count ?? 0,
                Dimensions  = idx?.Dimensions ?? 0,
                BuiltAtUtc  = idx?.BuiltAtUtc,
                EngineId    = idx?.EngineId,
                Model       = idx?.Model,
                IndexPath   = path,
                SizeBytes   = size
            };
        }
    }
}
