using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rxns.Ai;
using Rxns.AppStatus.Host.Ai.Workspace;

namespace Rxns.AppStatus.Host.Ai.Tools
{
    /// <summary>
    /// Tool: semantic-search the workspace knowledge indexes built via the
    /// "Train" button in Settings. The model dispatches with a natural-language
    /// query; we embed it with the same engine that built the index and run
    /// cosine search against each per-root index, merging top-k by score.
    /// </summary>
    public class WorkspaceKnowledgeSearchTool : IAiToolHandler
    {
        private readonly AiOptions _options;
        private readonly WorkspaceIndexer _indexer;
        private readonly IEnumerable<IAiEmbeddingsEngine> _embeddingsEngines;

        public WorkspaceKnowledgeSearchTool(
            AiOptions options,
            WorkspaceIndexer indexer,
            IEnumerable<IAiEmbeddingsEngine> embeddingsEngines)
        {
            _options = options ?? new AiOptions();
            _indexer = indexer;
            _embeddingsEngines = embeddingsEngines ?? Array.Empty<IAiEmbeddingsEngine>();
        }

        public AiToolDefinition Definition { get; } = new AiToolDefinition
        {
            Name = "search_workspace_knowledge",
            Description = "Semantic search over the workspace knowledge indexes built via the 'Train' button. Use when you need the design rationale, runbook, or specific doc passages from the project — better than reading every file in turn. Returns the top-k matching chunks with file path + line range so you can read more around them via workspace_read_file.",
            RequiresWriteAccess = false,
            InputSchemaJson = @"{
  ""type"": ""object"",
  ""properties"": {
    ""query"": { ""type"": ""string"", ""description"": ""Natural-language query, e.g. 'how do I restart the support portal' or 'what's the data flow for monitor suggestions'."" },
    ""root"":  { ""type"": ""string"", ""description"": ""Restrict to one workspace root (e.g. 'C:/src/myrepo'). Omit to search across all built indexes."" },
    ""topK"":  { ""type"": ""integer"", ""description"": ""Max results merged across roots (default 5, hard cap 20)."" }
  },
  ""required"": [""query""]
}"
        };

        public async Task<AiToolResult> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
        {
            try
            {
                var args = string.IsNullOrWhiteSpace(argumentsJson) ? new JObject() : JObject.Parse(argumentsJson);
                var query = (string)args["query"];
                if (string.IsNullOrWhiteSpace(query))
                    return new AiToolResult { IsError = true, ErrorMessage = "query is required" };

                var rootArg = (string)args["root"];
                var topK = (int?)args["topK"] ?? 5;
                if (topK < 1) topK = 1; if (topK > 20) topK = 20;

                var rootsToSearch = new List<string>();
                if (!string.IsNullOrWhiteSpace(rootArg))
                {
                    var resolved = WorkspacePathGuard.TryResolve(rootArg, _options.WorkspaceRoots, out var err);
                    if (resolved == null) return new AiToolResult { IsError = true, ErrorMessage = err };
                    rootsToSearch.Add(resolved);
                }
                else
                {
                    rootsToSearch.AddRange((_options.WorkspaceRoots ?? new List<string>()).Where(r => !string.IsNullOrWhiteSpace(r)));
                }

                // Load each root's index; collect the engine ids actually used
                // to build them so we know which engine to embed the query with.
                var perRoot = new List<(string Root, WorkspaceKnowledgeIndex Index)>();
                foreach (var r in rootsToSearch)
                {
                    var idx = _indexer.Load(r);
                    if (idx != null && idx.Chunks != null && idx.Chunks.Count > 0) perRoot.Add((r, idx));
                }
                if (perRoot.Count == 0)
                    return new AiToolResult
                    {
                        OutputJson = JsonConvert.SerializeObject(new
                        {
                            matches = Array.Empty<object>(),
                            note = "no built indexes — open Settings → Workspace → Knowledge index and click Build for at least one root"
                        })
                    };

                // Use the embedding engine that built the first index — we
                // assume the operator uses one engine consistently. If indexes
                // are mixed, query the matching engine per group (future).
                var engineId = perRoot[0].Index.EngineId;
                var engine = _embeddingsEngines.FirstOrDefault(e => string.Equals(e.EngineId, engineId, StringComparison.OrdinalIgnoreCase) && e.IsAvailable);
                if (engine == null)
                    return new AiToolResult
                    {
                        IsError = true,
                        ErrorMessage = "embeddings engine '" + engineId + "' used to build the index isn't currently available. Start the backend or rebuild the index with a different engine."
                    };

                IReadOnlyList<float[]> qEmbeds;
                try { qEmbeds = await engine.EmbedAsync(new[] { query }, ct).ConfigureAwait(false); }
                catch (Exception ex) { return new AiToolResult { IsError = true, ErrorMessage = "embeddings call failed: " + ex.Message }; }

                if (qEmbeds == null || qEmbeds.Count == 0 || qEmbeds[0] == null || qEmbeds[0].Length == 0)
                    return new AiToolResult { IsError = true, ErrorMessage = "embeddings engine returned empty vector" };

                var q = qEmbeds[0];

                // Fan-out + merge top-k by score across roots.
                var merged = new List<object>();
                var scoredAll = new List<(WorkspaceKnowledgeIndex.IndexedChunk Chunk, double Score, string Root)>();
                foreach (var (root, idx) in perRoot)
                {
                    foreach (var hit in idx.Search(q, topK))
                        scoredAll.Add((hit.Chunk, hit.Score, root));
                }
                var top = scoredAll.OrderByDescending(s => s.Score).Take(topK).ToList();

                return new AiToolResult
                {
                    OutputJson = JsonConvert.SerializeObject(new
                    {
                        engine = engineId,
                        matches = top.Select(t => new
                        {
                            root          = t.Root,
                            path          = t.Chunk.Path,
                            relativePath  = t.Chunk.RelativePath,
                            lineStart     = t.Chunk.LineStart,
                            lineEnd       = t.Chunk.LineEnd,
                            score         = Math.Round(t.Score, 4),
                            text          = t.Chunk.Text
                        }).ToList()
                    })
                };
            }
            catch (Exception ex)
            {
                return new AiToolResult { IsError = true, ErrorMessage = ex.Message };
            }
        }
    }
}
