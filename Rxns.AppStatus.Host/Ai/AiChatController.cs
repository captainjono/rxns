using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Rxns.Ai;
using Rxns.AppStatus.Host.Ai.Discovery;
using Rxns.AppStatus.Host.Ai.Workspace;

namespace Rxns.AppStatus.Host.Ai
{
    /// <summary>
    /// REST surface for the portal AI chat pane.
    ///
    /// <para>Endpoints:</para>
    /// <list type="bullet">
    /// <item><c>POST /api/ai/chat</c> — single completion. Caller passes an
    /// optional <c>engine</c> id; the factory dispatches to that engine.
    /// On a <c>tool_use</c> stop reason, the controller runs the requested
    /// tools and re-prompts once before returning (same auto-resolve pattern
    /// that worked for Claude-only).</item>
    /// <item><c>GET /api/ai/info</c> — engines (with availability/cost),
    /// default engine id, read-only state, tool list.</item>
    /// <item><c>GET /api/ai/engines</c> — flat list suitable for an engine
    /// picker dropdown.</item>
    /// </list>
    /// </summary>
    [ApiController]
    [Route("api/ai")]
    public class AiChatController : ControllerBase
    {
        private readonly AiChatEngineFactory _engines;
        private readonly AiToolRegistry _tools;
        private readonly AiOptions _options;
        private readonly DynamicAiEngineRegistry _dynamic;
        private readonly AiEngineLocalConfigStore _store;
        private readonly AiEngineScanner _scanner;
        private readonly IEnumerable<IAiEngineDiscoveryAdapter> _discoveryAdapters;
        private readonly WorkspaceScanner _workspace;
        private readonly WorkspaceIndexer _indexer;
        private readonly IEnumerable<IAiEmbeddingsEngine> _embeddingsEngines;

        public AiChatController(
            AiChatEngineFactory engines,
            AiToolRegistry tools,
            AiOptions options,
            DynamicAiEngineRegistry dynamicRegistry,
            AiEngineLocalConfigStore store,
            AiEngineScanner scanner,
            IEnumerable<IAiEngineDiscoveryAdapter> discoveryAdapters,
            WorkspaceScanner workspace,
            WorkspaceIndexer indexer,
            IEnumerable<IAiEmbeddingsEngine> embeddingsEngines)
        {
            _engines = engines;
            _tools = tools;
            _options = options ?? new AiOptions();
            _dynamic = dynamicRegistry;
            _store = store;
            _scanner = scanner;
            _discoveryAdapters = discoveryAdapters ?? new IAiEngineDiscoveryAdapter[0];
            _workspace = workspace;
            _indexer = indexer;
            _embeddingsEngines = embeddingsEngines ?? new IAiEmbeddingsEngine[0];
        }

        public class ChatPayload
        {
            public IList<AiChatMessage> Messages { get; set; }

            /// <summary>Engine id to route to (e.g. "ollama-default", "claude-default").
            /// Null/empty → factory falls back to <see cref="AiOptions.DefaultEngineId"/>.</summary>
            public string Engine { get; set; }

            /// <summary>Optional per-request model override (e.g. switch from the
            /// engine's default <c>llama3.1</c> to <c>qwen2.5-coder:7b</c>).</summary>
            public string Model { get; set; }

            /// <summary>When true (default), execute any tool_use returned and
            /// feed the result back once before returning.</summary>
            public bool? ResolveTools { get; set; }

            /// <summary>Advertise the tool registry to the model on this request?
            /// Default true. Set false for engines/models known to mis-handle
            /// tool descriptions — e.g. qwen2.5-7b-instruct on Foundry/NPU emits
            /// tool calls as inline text instead of using the OpenAI
            /// <c>tool_calls</c> field, garbling the response.</summary>
            public bool? AllowToolCalls { get; set; }
        }

        [HttpGet("info")]
        public IActionResult Info()
        {
            var all = _engines.All();
            return Ok(new
            {
                defaultEngine = ResolveDefaultEngineId(),
                readOnly = _options.ReadOnly,
                engines = all.Select(e => new
                {
                    id = e.EngineId,
                    label = e.Label,
                    kind = e.Kind,
                    model = e.ModelId,
                    available = e.IsAvailable,
                    cost = CostHint(e.Kind)
                }).ToList(),
                tools = _tools.List().Select(h => new
                {
                    name = h.Definition.Name,
                    description = h.Definition.Description,
                    requiresWriteAccess = h.Definition.RequiresWriteAccess
                }).ToList()
            });
        }

        [HttpGet("engines")]
        public IActionResult Engines()
        {
            return Ok(_engines.All().Select(e => new
            {
                id = e.EngineId,
                label = e.Label,
                kind = e.Kind,
                model = e.ModelId,
                available = e.IsAvailable,
                cost = CostHint(e.Kind),
                isDefault = string.Equals(e.EngineId, ResolveDefaultEngineId(), StringComparison.OrdinalIgnoreCase)
            }).ToList());
        }

        // ── workspace (multi-root + auto-discovery) ─────────────────────────

        public class WorkspaceConfigBody
        {
            public List<string> Roots { get; set; }
            public List<string> DiscoveryPatterns { get; set; }
            public List<string> SelectedKnowledgeFiles { get; set; }
        }

        [HttpGet("workspace/config")]
        public IActionResult GetWorkspaceConfig()
        {
            return Ok(new
            {
                roots                  = _options.WorkspaceRoots ?? new List<string>(),
                discoveryPatterns      = (_options.DiscoveryPatterns != null && _options.DiscoveryPatterns.Count > 0) ? _options.DiscoveryPatterns : WorkspaceScanner.DefaultPatterns,
                selectedKnowledgeFiles = _options.SelectedKnowledgeFiles ?? new List<string>(),
                knowledgeBudgetBytes   = _options.KnowledgeBudgetBytes,
                defaultPatterns        = WorkspaceScanner.DefaultPatterns
            });
        }

        [HttpPut("workspace/config")]
        public IActionResult PutWorkspaceConfig([FromBody] WorkspaceConfigBody body)
        {
            if (body == null) return BadRequest(new { error = "body required" });

            // null = leave alone; empty list = clear
            if (body.Roots != null)                  _options.WorkspaceRoots = body.Roots;
            if (body.DiscoveryPatterns != null)      _options.DiscoveryPatterns = body.DiscoveryPatterns;
            if (body.SelectedKnowledgeFiles != null) _options.SelectedKnowledgeFiles = body.SelectedKnowledgeFiles;

            _store.SaveWorkspace(body.Roots, body.DiscoveryPatterns, body.SelectedKnowledgeFiles);

            return Ok(new
            {
                roots                  = _options.WorkspaceRoots,
                discoveryPatterns      = _options.DiscoveryPatterns,
                selectedKnowledgeFiles = _options.SelectedKnowledgeFiles
            });
        }

        // ── embeddings engines (for RAG index build) ──────────────────────

        [HttpGet("workspace/embeddings-engines")]
        public IActionResult ListEmbeddingsEngines()
        {
            var all = (_embeddingsEngines ?? new IAiEmbeddingsEngine[0]).ToList();
            var declared = (_options.EmbeddingsEngines ?? new List<AiEngineCfg>())
                .Where(e => e != null && !string.IsNullOrWhiteSpace(e.Id)).ToList();
            // Engines persisted to cfg but not yet wired into DI (added at
            // runtime → require a restart) surface as "pendingRestart".
            var loadedIds = new HashSet<string>(all.Select(e => e.EngineId), StringComparer.OrdinalIgnoreCase);
            return Ok(new
            {
                defaultEngineId = _options.DefaultEmbeddingsEngineId,
                loaded = all.Select(e => new
                {
                    id = e.EngineId, label = e.Label, kind = e.Kind, model = e.ModelId, available = e.IsAvailable
                }).ToList(),
                pendingRestart = declared.Where(d => !loadedIds.Contains(d.Id))
                    .Select(d => new { id = d.Id, label = d.Label, kind = d.Kind, model = d.Model })
                    .ToList()
            });
        }

        public class AddEmbeddingsBody
        {
            public string Id { get; set; }
            public string Kind { get; set; }
            public string Label { get; set; }
            public string Endpoint { get; set; }
            public string ApiKey { get; set; }
            public string Model { get; set; }
            public bool   MakeDefault { get; set; }
        }

        [HttpPost("workspace/embeddings-engines")]
        public IActionResult AddEmbeddingsEngine([FromBody] AddEmbeddingsBody body)
        {
            if (body == null) return BadRequest(new { error = "body required" });
            if (string.IsNullOrWhiteSpace(body.Kind)) return BadRequest(new { error = "kind required (ollama | foundry)" });
            if (string.IsNullOrWhiteSpace(body.Model)) return BadRequest(new { error = "model required (e.g. nomic-embed-text)" });

            var cfg = new AiEngineCfg
            {
                Id       = string.IsNullOrWhiteSpace(body.Id) ? body.Kind.ToLowerInvariant() + "-embed-" + Guid.NewGuid().ToString("N").Substring(0, 6) : body.Id,
                Kind     = body.Kind.Trim().ToLowerInvariant(),
                Label    = body.Label,
                Endpoint = body.Endpoint,
                ApiKey   = body.ApiKey,
                Model    = body.Model
            };

            _options.EmbeddingsEngines.RemoveAll(e => string.Equals(e.Id, cfg.Id, StringComparison.OrdinalIgnoreCase));
            _options.EmbeddingsEngines.Add(cfg);
            _store.UpsertEmbeddingsEngine(cfg);
            if (body.MakeDefault) _options.DefaultEmbeddingsEngineId = cfg.Id;

            return Ok(new
            {
                id = cfg.Id, kind = cfg.Kind, model = cfg.Model,
                pendingRestart = !(_embeddingsEngines ?? new IAiEmbeddingsEngine[0]).Any(e => string.Equals(e.EngineId, cfg.Id, StringComparison.OrdinalIgnoreCase)),
                hint = "Engine persisted to appstatus.local.config. Restart the portal to activate (runtime embedding-engine registration is a future enhancement)."
            });
        }

        [HttpDelete("workspace/embeddings-engines/{id}")]
        public IActionResult RemoveEmbeddingsEngine(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return BadRequest(new { error = "id required" });
            _options.EmbeddingsEngines.RemoveAll(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));
            _store.RemoveEmbeddingsEngine(id);
            if (string.Equals(_options.DefaultEmbeddingsEngineId, id, StringComparison.OrdinalIgnoreCase))
                _options.DefaultEmbeddingsEngineId = null;
            return Ok(new { id, removed = true });
        }

        // ── knowledge index (build / clear / status) ──────────────────────

        [HttpGet("workspace/index")]
        public IActionResult IndexStatus()
        {
            var roots = (_options.WorkspaceRoots ?? new List<string>()).Where(r => !string.IsNullOrWhiteSpace(r)).ToList();
            return Ok(new
            {
                defaultEmbeddingsEngineId = _options.DefaultEmbeddingsEngineId,
                roots = roots.Select(r => _indexer.Status(r)).ToList()
            });
        }

        public class IndexRootBody { public string Root { get; set; } public string EmbeddingsEngineId { get; set; } }

        [HttpPost("workspace/index/build")]
        public async Task<IActionResult> BuildIndex([FromBody] IndexRootBody body, CancellationToken ct)
        {
            if (body == null || string.IsNullOrWhiteSpace(body.Root))
                return BadRequest(new { error = "root required" });

            var resolvedRoot = WorkspacePathGuard.TryResolve(body.Root, _options.WorkspaceRoots, out var err);
            if (resolvedRoot == null) return BadRequest(new { error = err });

            // Files to index: operator's selection limited to this root.
            var selected = (_options.SelectedKnowledgeFiles ?? new List<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p) && p.StartsWith(resolvedRoot, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (selected.Count == 0)
                return BadRequest(new { error = "no files selected under this root — tick at least one in Workspace settings" });

            var engineId = !string.IsNullOrWhiteSpace(body.EmbeddingsEngineId)
                ? body.EmbeddingsEngineId
                : _options.DefaultEmbeddingsEngineId;
            var engine = (_embeddingsEngines ?? new IAiEmbeddingsEngine[0])
                .FirstOrDefault(e => string.IsNullOrWhiteSpace(engineId) ? e.IsAvailable : string.Equals(e.EngineId, engineId, StringComparison.OrdinalIgnoreCase));
            if (engine == null)
                return BadRequest(new { error = "no embeddings engine available; add one in Settings and restart the portal" });

            try
            {
                var idx = await _indexer.BuildAsync(resolvedRoot, selected, engine, ct);
                return Ok(new
                {
                    root = resolvedRoot,
                    chunks = idx.Chunks.Count,
                    dimensions = idx.Dimensions,
                    engine = idx.EngineId,
                    model = idx.Model,
                    builtAtUtc = idx.BuiltAtUtc
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpDelete("workspace/index")]
        public IActionResult ClearIndex([FromBody] IndexRootBody body)
        {
            if (body == null || string.IsNullOrWhiteSpace(body.Root))
                return BadRequest(new { error = "root required" });
            var resolvedRoot = WorkspacePathGuard.TryResolve(body.Root, _options.WorkspaceRoots, out var err);
            if (resolvedRoot == null) return BadRequest(new { error = err });
            _indexer.Clear(resolvedRoot);
            return Ok(new { root = resolvedRoot, cleared = true });
        }

        [HttpGet("workspace/scan")]
        public IActionResult ScanWorkspace()
        {
            var roots    = _options.WorkspaceRoots ?? new List<string>();
            var patterns = _options.DiscoveryPatterns ?? new List<string>();
            var hits     = _workspace.Scan(roots, patterns);
            return Ok(new
            {
                roots = roots,
                patterns = (patterns.Count > 0 ? patterns : WorkspaceScanner.DefaultPatterns).ToList(),
                files = hits.Select(h => new
                {
                    root         = h.Root,
                    relativePath = h.RelativePath,
                    absolutePath = h.AbsolutePath,
                    sizeBytes    = h.SizeBytes,
                    modifiedUtc  = h.ModifiedUtc
                })
            });
        }

        public class ProjectContextBody { public string Text { get; set; } }

        [HttpGet("project-context")]
        public IActionResult GetProjectContext()
        {
            return Ok(new
            {
                text = _options.ProjectContext ?? "",
                hint = "Free-form repo knowledge (CLAUDE.md style) prepended to every system prompt. " +
                       "Persists to appstatus.local.config so it survives restart."
            });
        }

        [HttpPut("project-context")]
        public IActionResult SetProjectContext([FromBody] ProjectContextBody body)
        {
            var text = body?.Text ?? "";
            // Hard cap so a runaway paste doesn't blow out every model's
            // context window on every turn. 64 KB is roughly 16k tokens —
            // plenty for a project README without dominating the prompt.
            if (text.Length > 64 * 1024)
                return BadRequest(new { error = "project context exceeds 64KB; trim before saving" });

            _options.ProjectContext = text;
            _store.SaveProjectContext(text);
            return Ok(new { length = text.Length, persistedTo = "appstatus.local.config" });
        }

        public class AddEngineBody
        {
            public string Id { get; set; }
            public string Kind { get; set; }       // claude | ollama | foundry | cli
            public string Label { get; set; }
            public string Endpoint { get; set; }
            public string ApiKey { get; set; }
            public string Model { get; set; }
            public bool Default { get; set; }
        }

        [HttpPost("engines")]
        public IActionResult AddEngine([FromBody] AddEngineBody body)
        {
            if (body == null) return BadRequest(new { error = "body required" });
            if (string.IsNullOrWhiteSpace(body.Kind)) return BadRequest(new { error = "kind is required (claude|ollama|foundry|cli)" });

            var cfg = new AiEngineCfg
            {
                Id = string.IsNullOrWhiteSpace(body.Id) ? AutoId(body.Kind, body.Endpoint) : body.Id,
                Kind = body.Kind.Trim().ToLowerInvariant(),
                Label = body.Label,
                Endpoint = body.Endpoint,
                ApiKey = body.ApiKey,
                Model = body.Model,
                Default = body.Default
            };

            IAiChatEngine engine;
            try { engine = AiModule.BuildEngine(cfg, _tools); }
            catch (Exception ex) { return BadRequest(new { error = ex.Message }); }

            _dynamic.Register(cfg);
            _store.Upsert(cfg);

            return Ok(new
            {
                id = engine.EngineId,
                kind = engine.Kind,
                label = engine.Label,
                model = engine.ModelId,
                available = engine.IsAvailable,
                persistedTo = "appstatus.local.config"
            });
        }

        [HttpDelete("engines/{id}")]
        public IActionResult RemoveEngine(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return BadRequest(new { error = "id required" });
            var dyn = _dynamic.Unregister(id);
            _store.Remove(id);
            return Ok(new { id, removedFromMemory = dyn, removedFromConfig = true });
        }

        public class ScanBody
        {
            public string Cidr { get; set; }
            public int[] Ports { get; set; }
            public bool Confirmed { get; set; }   // UI must set true after the warn+confirm step
        }

        [HttpPost("engines/scan")]
        public async Task<IActionResult> ScanEngines([FromBody] ScanBody body, CancellationToken ct)
        {
            if (body == null || string.IsNullOrWhiteSpace(body.Cidr))
                return BadRequest(new { error = "cidr required (e.g. '192.168.1.0/24')" });
            if (!body.Confirmed)
                return BadRequest(new { error = "scan requires explicit confirmation — set Confirmed=true after warning the operator" });

            List<string> hosts;
            try { hosts = AiEngineScanner.ExpandCidr(body.Cidr).ToList(); }
            catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }

            // Hard ceiling so a typo can't sweep /8.
            if (hosts.Count > 4096)
                return BadRequest(new { error = "CIDR expands to " + hosts.Count + " hosts — refusing (max 4096). Use a narrower prefix." });

            var results = await _scanner.ScanAsync(body.Cidr, body.Ports, parallelism: 64, ct: ct).ConfigureAwait(false);
            return Ok(new
            {
                scanned = hosts.Count,
                ports = body.Ports != null && body.Ports.Length > 0 ? body.Ports : AiEngineScanner.DefaultPorts,
                discovered = results.Select(r => new
                {
                    url = r.Url,
                    kind = r.Kind,
                    models = r.Models,
                    latencyMs = r.LatencyMs
                }).ToList()
            });
        }

        /// <summary>
        /// Aggregate every registered <see cref="IAiEngineDiscoveryAdapter"/>'s
        /// findings into one flat list. Failures of individual adapters don't
        /// abort the whole pass — each adapter is wrapped in a try/catch so a
        /// crashy vendor CLI can't block another adapter from succeeding.
        /// </summary>
        [HttpPost("engines/discover")]
        public async Task<IActionResult> DiscoverEngines(CancellationToken ct)
        {
            var all = new List<object>();
            var adapters = _discoveryAdapters.ToList();

            foreach (var adapter in adapters)
            {
                try
                {
                    var found = await adapter.DiscoverAsync(ct).ConfigureAwait(false);
                    foreach (var f in found ?? new List<DiscoveredEngine>())
                    {
                        if (f == null) continue;
                        all.Add(new
                        {
                            adapter = adapter.Id,
                            adapterLabel = adapter.Label,
                            url = f.Url,
                            kind = f.Kind,
                            models = f.Models ?? new List<string>(),
                            latencyMs = f.LatencyMs
                        });
                    }
                }
                catch (Exception ex)
                {
                    all.Add(new
                    {
                        adapter = adapter.Id,
                        adapterLabel = adapter.Label,
                        error = ex.GetType().Name + ": " + ex.Message
                    });
                }
            }

            return Ok(new
            {
                adapters = adapters.Select(a => new { id = a.Id, label = a.Label }).ToList(),
                discovered = all
            });
        }

        private static string AutoId(string kind, string endpoint)
        {
            var host = "host";
            try
            {
                if (!string.IsNullOrWhiteSpace(endpoint))
                {
                    var uri = new Uri(endpoint.Contains("://") ? endpoint : "http://" + endpoint);
                    host = uri.Host + (uri.Port == 80 || uri.Port == 443 ? "" : "-" + uri.Port);
                }
            }
            catch { /* keep fallback */ }
            return (kind ?? "engine").ToLowerInvariant() + "-" + host.Replace('.', '-');
        }

        [HttpGet("engines/{id}/models")]
        public async Task<IActionResult> Models(string id, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(id)) return BadRequest("engine id required");

            // Look up engine regardless of availability — the UI wants to render the
            // engine's known default even when the backend is offline (e.g. Foundry
            // not running), so the user sees something to pick. Pick() throws for
            // unavailable engines; All() doesn't filter.
            var engine = _engines.All().FirstOrDefault(e => string.Equals(e.EngineId, id, StringComparison.OrdinalIgnoreCase));
            if (engine == null) return NotFound(new { error = "unknown engine id: " + id });

            IReadOnlyList<string> models = new List<string>();
            string warning = null;
            if (engine.IsAvailable)
            {
                try { models = await engine.ListModelsAsync(ct).ConfigureAwait(false); }
                catch (Exception ex) { warning = "ListModels failed: " + ex.Message; }
            }
            else
            {
                warning = engine.Kind + " engine '" + id + "' is unavailable — start the backend (or set the *_URL/*_API_KEY env var) to populate models.";
            }

            return Ok(new
            {
                engineId = engine.EngineId,
                kind = engine.Kind,
                available = engine.IsAvailable,
                defaultModel = engine.ModelId,
                models = models,
                warning = warning
            });
        }

        [HttpPost("chat")]
        public async Task<IActionResult> Chat([FromBody] ChatPayload payload, CancellationToken ct)
        {
            if (payload?.Messages == null || payload.Messages.Count == 0)
                return BadRequest("Messages array is required.");

            IAiChatEngine engine;
            try { engine = _engines.Pick(payload.Engine); }
            catch (InvalidOperationException ex) { return StatusCode(503, ex.Message); }

            var allowTools = payload.AllowToolCalls ?? true;
            // Build the system prompt in three layers:
            //   1. base persona (tool-agnostic)
            //   2. ProjectContext — operator-supplied repo knowledge (start
            //      commands, conventions, etc.). Acts like a CLAUDE.md. Empty
            //      by default; populated via PUT /api/ai/project-context.
            //   3. ToolsAppendix — only when tools are actually advertised, so
            //      models that can't see tools aren't told to call them
            //      (qwen2.5-7b hallucination guard).
            var systemPrompt = _options.SystemPrompt;
            if (!string.IsNullOrWhiteSpace(_options.ProjectContext))
                systemPrompt += "\n\nProject context:\n" + _options.ProjectContext.Trim();

            // Workspace knowledge — operator-selected files from auto-discovery.
            // Inlined with absolute-path delimiters so the model knows what each
            // block is. Bounded by KnowledgeBudgetBytes so a 100-file selection
            // doesn't torch the prompt window.
            if (_options.SelectedKnowledgeFiles != null && _options.SelectedKnowledgeFiles.Count > 0 && _workspace != null)
            {
                var knowledge = _workspace.BuildKnowledgePreamble(_options.SelectedKnowledgeFiles, _options.KnowledgeBudgetBytes);
                if (!string.IsNullOrWhiteSpace(knowledge))
                    systemPrompt += "\n\nWorkspace knowledge:" + knowledge;
            }

            if (allowTools && !string.IsNullOrWhiteSpace(_options.ToolsAppendix))
                systemPrompt += "\n\n" + _options.ToolsAppendix;
            var request = new AiChatRequest
            {
                Messages = payload.Messages,
                SystemPrompt = systemPrompt,
                AllowToolCalls = allowTools,
                ModelOverride = payload.Model
            };

            AiChatResponse response;
            try { response = await engine.CompleteAsync(request, ct).ConfigureAwait(false); }
            catch (Exception ex) { return StatusCode(500, engine.Kind + " call failed: " + ex.Message); }

            var resolve = payload.ResolveTools ?? true;
            if (resolve && response.StopReason == "tool_use" && response.ToolCalls != null && response.ToolCalls.Count > 0)
            {
                var historyForReply = new List<AiChatMessage>(payload.Messages);

                if (!string.IsNullOrEmpty(response.AssistantText))
                {
                    historyForReply.Add(new AiChatMessage { Role = "assistant", Content = response.AssistantText });
                }

                foreach (var call in response.ToolCalls)
                {
                    var handler = _tools.FindByName(call.Name);
                    string resultJson;
                    if (handler == null)
                    {
                        resultJson = "{\"error\":\"tool not registered or filtered (read-only mode?)\"}";
                    }
                    else
                    {
                        try
                        {
                            var r = await handler.ExecuteAsync(call.ArgumentsJson, ct).ConfigureAwait(false);
                            resultJson = r.IsError
                                ? "{\"error\":\"" + (r.ErrorMessage ?? "tool failed").Replace("\"", "\\\"") + "\"}"
                                : (r.OutputJson ?? "{}");
                        }
                        catch (Exception ex)
                        {
                            resultJson = "{\"error\":\"" + ex.Message.Replace("\"", "\\\"") + "\"}";
                        }
                    }

                    historyForReply.Add(new AiChatMessage
                    {
                        Role = "tool",
                        ToolName = call.Name,
                        ToolUseId = call.Id,
                        Content = resultJson
                    });
                }

                var followup = new AiChatRequest
                {
                    Messages = historyForReply,
                    SystemPrompt = systemPrompt,
                    AllowToolCalls = allowTools,
                    ModelOverride = payload.Model
                };

                AiChatResponse second;
                try { second = await engine.CompleteAsync(followup, ct).ConfigureAwait(false); }
                catch (Exception ex)
                {
                    return Ok(new
                    {
                        firstTurn = response,
                        toolsResolved = true,
                        error = "follow-up " + engine.Kind + " call failed: " + ex.Message
                    });
                }

                return Ok(new
                {
                    firstTurn = response,
                    toolsResolved = true,
                    secondTurn = second,
                    messagesAfter = historyForReply
                });
            }

            return Ok(new { firstTurn = response, toolsResolved = false });
        }

        private string ResolveDefaultEngineId()
        {
            if (!string.IsNullOrWhiteSpace(_options.DefaultEngineId)) return _options.DefaultEngineId;
            var firstDefault = _options.Engines?.FirstOrDefault(e => e != null && e.Default);
            if (firstDefault != null) return firstDefault.Id;
            var firstAvailable = _engines.Available().FirstOrDefault();
            return firstAvailable?.EngineId;
        }

        private static string CostHint(string kind)
        {
            switch ((kind ?? "").ToLowerInvariant())
            {
                case "ollama":
                case "foundry":
                case "cli":
                    return "free";
                case "claude":
                    return "paid";
                default:
                    return "unknown";
            }
        }
    }
}
