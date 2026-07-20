using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Rxns.AppStatus.Host.Ai.Workspace
{
    /// <summary>
    /// Serializable on-disk knowledge index for one workspace root. Brute-force
    /// cosine search over an in-memory float[] array — fine for the file-count
    /// scales typical here (a few thousand chunks per repo). When this stops
    /// being fast enough we swap the storage layer to SQLite + sqlite-vec
    /// without changing the surface.
    ///
    /// <para>File layout: <c>&lt;root&gt;/.rxns/ai-knowledge/index.json</c> —
    /// single JSON document with the version, the engine/model used to embed
    /// (so we can warn on mismatch), and a flat chunks array. Lives next to
    /// the existing rxns DDD tape store.</para>
    /// </summary>
    public class WorkspaceKnowledgeIndex
    {
        public int      Version    { get; set; } = 1;
        public string   Root       { get; set; }
        public string   EngineId   { get; set; }
        public string   Model      { get; set; }
        public int      Dimensions { get; set; }
        public DateTime BuiltAtUtc { get; set; }
        public List<string> SelectedFilesAtBuild { get; set; } = new List<string>();
        public List<IndexedChunk> Chunks { get; set; } = new List<IndexedChunk>();

        public class IndexedChunk
        {
            public string Path         { get; set; }   // absolute
            public string RelativePath { get; set; }
            public int    LineStart    { get; set; }
            public int    LineEnd      { get; set; }
            public string Text         { get; set; }
            public float[] Embedding   { get; set; }
        }

        /// <summary>Where the per-root index file lives. Mirrors the .rxns
        /// convention used by the DDD tape store.</summary>
        public static string ResolveIndexPath(string root)
        {
            if (string.IsNullOrWhiteSpace(root)) return null;
            return Path.Combine(Path.GetFullPath(root), ".rxns", "ai-knowledge", "index.json");
        }

        public static WorkspaceKnowledgeIndex Load(string root)
        {
            var path = ResolveIndexPath(root);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try
            {
                var doc = File.ReadAllText(path);
                return JsonConvert.DeserializeObject<WorkspaceKnowledgeIndex>(doc);
            }
            catch { return null; }
        }

        public void Save()
        {
            var path = ResolveIndexPath(Root);
            if (path == null) throw new InvalidOperationException("Root is required");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented));
        }

        public static void Clear(string root)
        {
            var path = ResolveIndexPath(root);
            if (string.IsNullOrEmpty(path)) return;
            try { if (File.Exists(path)) File.Delete(path); } catch { /* best-effort */ }
        }

        /// <summary>Top-k cosine search against the in-memory chunks. O(N * D)
        /// per query — fine for thousands of chunks; we'd swap to ANN once
        /// real-world indexes outgrow that.</summary>
        public List<(IndexedChunk Chunk, double Score)> Search(float[] query, int topK = 5)
        {
            if (query == null || query.Length == 0 || Chunks == null || Chunks.Count == 0)
                return new List<(IndexedChunk, double)>();
            if (topK < 1) topK = 1;

            // Normalise query once.
            var qNorm = Norm(query);
            if (qNorm == 0) return new List<(IndexedChunk, double)>();

            var scored = new List<(IndexedChunk Chunk, double Score)>(Chunks.Count);
            foreach (var c in Chunks)
            {
                if (c.Embedding == null || c.Embedding.Length != query.Length) continue;
                var n = Norm(c.Embedding);
                if (n == 0) continue;
                var dot = Dot(query, c.Embedding);
                var sim = dot / (qNorm * n);
                scored.Add((c, sim));
            }
            return scored.OrderByDescending(s => s.Score).Take(topK).ToList();
        }

        private static double Dot(float[] a, float[] b)
        {
            double s = 0;
            var n = Math.Min(a.Length, b.Length);
            for (var i = 0; i < n; i++) s += a[i] * b[i];
            return s;
        }

        private static double Norm(float[] a)
        {
            double s = 0;
            for (var i = 0; i < a.Length; i++) s += a[i] * a[i];
            return Math.Sqrt(s);
        }
    }
}
