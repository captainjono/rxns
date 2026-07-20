using System;
using System.Collections.Generic;
using Rxns.Ai;
using Rxns.AppStatus.Host.AppInsights;
using Rxns.AppStatus.Host.Ai.Discovery;
using Rxns.AppStatus.Host.Ai.Tools;
using Rxns.Hosting;
using Rxns.Microservices;

namespace Rxns.AppStatus.Host.Ai
{
    /// <summary>
    /// Registers the AI chat backend (options + engines + tool registry +
    /// built-in read-only tools) into the portal's rxns container. Pulled in
    /// by <c>AppStatusHostApp</c>; embedding apps can also load it directly
    /// if they want the AI surface without the rest of AppStatus.
    ///
    /// <para>Config sources (last write wins):</para>
    /// <list type="number">
    /// <item><description><c>appstatus.config</c> — JSON file next to the host
    /// binary (or under <c>RXNS_APPSTATUS_CONFIG_DIR</c>). The <c>Ai</c> section
    /// declares one entry per engine the portal should know about (<see cref="AiEngineCfg"/>).</description></item>
    /// <item><description>Env-var conveniences:
    /// <c>CLAUDE_API_KEY</c> / <c>ANTHROPIC_API_KEY</c> auto-add a <c>claude-default</c> engine,
    /// <c>OLLAMA_URL</c> + <c>OLLAMA_MODEL</c> auto-add an <c>ollama-default</c> engine,
    /// <c>FOUNDRY_URL</c> + <c>FOUNDRY_MODEL</c> auto-add a <c>foundry-default</c> engine,
    /// <c>AI_DEFAULT_ENGINE</c> pins the default, <c>AI_READONLY</c> flips the read-only filter.</description></item>
    /// </list>
    ///
    /// <para>Engines from config are registered individually as
    /// <see cref="IAiChatEngine"/> with the cfg id as the autofac registration
    /// name; <see cref="AiChatEngineFactory"/> collects them via
    /// <c>IEnumerable&lt;IAiChatEngine&gt;</c>. Augmentation modules can register
    /// additional engines the same way they register tool handlers today.</para>
    /// </summary>
    public class AiModule : IAppModule
    {
        public IRxnLifecycle Load(IRxnLifecycle lifecycle)
        {
            var options = LoadOptions();

            lifecycle.CreatesOncePerApp(() => options);
            lifecycle.CreatesOncePerApp<AiToolRegistry>();
            lifecycle.CreatesOncePerApp<DynamicAiEngineRegistry>();
            lifecycle.CreatesOncePerApp<AiEngineLocalConfigStore>();
            lifecycle.CreatesOncePerApp<AiEngineScanner>();
            lifecycle.CreatesOncePerApp<Workspace.WorkspaceScanner>();
            lifecycle.CreatesOncePerApp<Workspace.WorkspaceChunker>();
            lifecycle.CreatesOncePerApp<Workspace.WorkspaceIndexer>();
            lifecycle.CreatesOncePerApp<AiChatEngineFactory>();

            // Discovery adapters — DI-collected like tools / monitor sources.
            // Augmentation modules can register their own adapter to surface
            // additional engine kinds (LM Studio, vLLM, mlx-server, …).
            lifecycle.CreatesOncePerAppAs<ProcessCommandRunner, ICommandRunner>();
            lifecycle.CreatesOncePerAppAs<FoundryServiceDiscoveryAdapter, IAiEngineDiscoveryAdapter>();

            // One IAiChatEngine registration per declared engine. Lambda gets the
            // resolver so we can pull AiToolRegistry (which OpenAiCompat + Claude
            // engines need to advertise tools to the model).
            foreach (var cfg in options.Engines ?? new List<AiEngineCfg>())
            {
                if (cfg == null || string.IsNullOrWhiteSpace(cfg.Id) || string.IsNullOrWhiteSpace(cfg.Kind))
                    continue;

                var local = cfg;  // capture for the closure
                lifecycle.CreatesOncePerApp<IAiChatEngine>(
                    c => BuildEngine(local, c.Resolve<AiToolRegistry>()),
                    preserveExisting: false,
                    named: local.Id);
            }

            // One IAiEmbeddingsEngine per declared embeddings entry.
            foreach (var cfg in options.EmbeddingsEngines ?? new List<AiEngineCfg>())
            {
                if (cfg == null || string.IsNullOrWhiteSpace(cfg.Id) || string.IsNullOrWhiteSpace(cfg.Kind))
                    continue;

                var local = cfg;
                lifecycle.CreatesOncePerApp<IAiEmbeddingsEngine>(
                    c => BuildEmbeddingsEngine(local),
                    preserveExisting: false,
                    named: local.Id);
            }

            // Built-in read-only tools — surfaced as IAiToolHandler so the
            // registry collects them automatically.
            lifecycle
                .CreatesOncePerAppAs<QueryLogsTool, IAiToolHandler>()
                .CreatesOncePerAppAs<ListSystemsTool, IAiToolHandler>()
                .CreatesOncePerAppAs<GetStatsTool, IAiToolHandler>()
                .CreatesOncePerAppAs<QueryErrorsTool, IAiToolHandler>()
                .CreatesOncePerAppAs<QueryAppInsightsTool, IAiToolHandler>()
                // Write-tagged — filtered out of the registry when ReadOnly=true.
                .CreatesOncePerAppAs<PublishEventTool, IAiToolHandler>()
                // Workspace JIT tools — scoped to AiOptions.WorkspaceRoots via
                // WorkspacePathGuard. Tool-capable models use these to fetch
                // specific files on demand instead of needing a CLAUDE.md-style
                // bulk attach.
                .CreatesOncePerAppAs<WorkspaceReadFileTool, IAiToolHandler>()
                .CreatesOncePerAppAs<WorkspaceListFilesTool, IAiToolHandler>()
                .CreatesOncePerAppAs<WorkspaceSearchTool, IAiToolHandler>()
                // Semantic search over the built knowledge indexes.
                .CreatesOncePerAppAs<WorkspaceKnowledgeSearchTool, IAiToolHandler>();

            return lifecycle;
        }

        /// <summary>Build an <see cref="IAiEmbeddingsEngine"/> from a cfg entry.
        /// Same factory pattern as the chat engines — kind drives the concrete
        /// class. Future Claude+Voyage support drops in here.</summary>
        public static IAiEmbeddingsEngine BuildEmbeddingsEngine(AiEngineCfg cfg)
        {
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));
            switch ((cfg.Kind ?? "").ToLowerInvariant())
            {
                case "ollama":  return new OllamaEmbeddingsEngine(cfg);
                case "foundry": return new FoundryEmbeddingsEngine(cfg);
                default:
                    throw new InvalidOperationException(
                        "Unsupported embeddings engine kind '" + cfg.Kind + "' (id: " + cfg.Id + "). " +
                        "Supported: ollama, foundry.");
            }
        }

        /// <summary>Build an <see cref="IAiChatEngine"/> from a cfg entry —
        /// shared between static (startup) and dynamic (runtime add) paths.
        /// Public so <see cref="DynamicAiEngineRegistry"/> can reuse it.</summary>
        public static IAiChatEngine BuildEngine(AiEngineCfg cfg, AiToolRegistry tools)
        {
            switch ((cfg.Kind ?? "").ToLowerInvariant())
            {
                case "claude":  return new ClaudeApiAiEngine(cfg, tools);
                case "ollama":  return new OllamaAiEngine(cfg, tools);
                case "foundry": return new FoundryAiEngine(cfg, tools);
                case "cli":     return new ClaudeProcessAiEngine(cfg);
                default:
                    throw new InvalidOperationException(
                        "Unknown AI engine kind '" + cfg.Kind + "' (id: " + cfg.Id + "). " +
                        "Supported: claude, ollama, foundry, cli.");
            }
        }

        private static AiOptions LoadOptions()
        {
            var opts = new AiOptions();

            // 1. appstatus.config (Ai section) — multi-engine declaration.
            var rxnCfg = AppInsightsRxnCfg.Loader.ResolveRaw();
            var fromCfg = rxnCfg?.Ai;
            if (fromCfg != null)
            {
                if (fromCfg.Engines != null) opts.Engines.AddRange(fromCfg.Engines);
                if (!string.IsNullOrWhiteSpace(fromCfg.DefaultEngineId)) opts.DefaultEngineId = fromCfg.DefaultEngineId;
                if (!string.IsNullOrWhiteSpace(fromCfg.SystemPrompt)) opts.SystemPrompt = fromCfg.SystemPrompt;
                if (fromCfg.ReadOnly.HasValue) opts.ReadOnly = fromCfg.ReadOnly.Value;
            }

            // 2. appstatus.local.config overlay — operator-added engines persisted
            //    via the bubble's Settings tab. Survives base-file edits because
            //    we only mutate the local sibling, never the base.
            try
            {
                var store = new AiEngineLocalConfigStore();
                foreach (var e in store.Load())
                {
                    if (e == null || string.IsNullOrWhiteSpace(e.Id)) continue;
                    opts.Engines.RemoveAll(x => string.Equals(x.Id, e.Id, StringComparison.OrdinalIgnoreCase));
                    opts.Engines.Add(e);
                }
                var projectCtx = store.LoadProjectContext();
                if (!string.IsNullOrWhiteSpace(projectCtx)) opts.ProjectContext = projectCtx;

                var ws = store.LoadWorkspace();
                if (ws.Roots.Count > 0)                  opts.WorkspaceRoots = ws.Roots;
                if (ws.DiscoveryPatterns.Count > 0)      opts.DiscoveryPatterns = ws.DiscoveryPatterns;
                if (ws.SelectedKnowledgeFiles.Count > 0) opts.SelectedKnowledgeFiles = ws.SelectedKnowledgeFiles;

                var embeds = store.LoadEmbeddingsEngines();
                foreach (var e in embeds)
                {
                    if (e == null || string.IsNullOrWhiteSpace(e.Id)) continue;
                    opts.EmbeddingsEngines.RemoveAll(x => string.Equals(x.Id, e.Id, StringComparison.OrdinalIgnoreCase));
                    opts.EmbeddingsEngines.Add(e);
                }
            }
            catch { /* malformed local cfg — ignore; the writer logs */ }

            // 3. Env-var conveniences — useful in dev and k8s deployments where
            //    minting an appstatus.config is overkill.
            AddClaudeFromEnv(opts);
            AddOllamaFromEnv(opts);
            AddFoundryFromEnv(opts);

            var defaultEngine = Environment.GetEnvironmentVariable("AI_DEFAULT_ENGINE");
            if (!string.IsNullOrWhiteSpace(defaultEngine)) opts.DefaultEngineId = defaultEngine;

            var readOnly = Environment.GetEnvironmentVariable("AI_READONLY");
            if (!string.IsNullOrWhiteSpace(readOnly) && bool.TryParse(readOnly, out var b)) opts.ReadOnly = b;

            return opts;
        }

        private static void AddClaudeFromEnv(AiOptions opts)
        {
            var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
                         ?? Environment.GetEnvironmentVariable("CLAUDE_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey)) return;
            if (HasEngineOfKind(opts, "claude")) return;

            opts.Engines.Add(new AiEngineCfg
            {
                Id = "claude-default",
                Kind = "claude",
                Label = "Claude · Haiku 4.5",
                Model = Environment.GetEnvironmentVariable("CLAUDE_MODEL") ?? "claude-haiku-4-5",
                ApiKey = apiKey
            });
        }

        private static void AddOllamaFromEnv(AiOptions opts)
        {
            // Only register when at least one Ollama env var is explicitly set —
            // no "always-on" defaults. The engine list should reflect what the
            // operator has actually opted into or scanned; phantom entries are
            // confusing and make the picker harder to reason about.
            var url = Environment.GetEnvironmentVariable("OLLAMA_URL");
            var model = Environment.GetEnvironmentVariable("OLLAMA_MODEL");
            if (string.IsNullOrWhiteSpace(url) && string.IsNullOrWhiteSpace(model)) return;
            if (HasEngineOfKind(opts, "ollama")) return;

            var resolvedModel = string.IsNullOrWhiteSpace(model) ? "llama3.1" : model;
            opts.Engines.Add(new AiEngineCfg
            {
                Id = "ollama-env",
                Kind = "ollama",
                Label = "Ollama · " + resolvedModel,
                Endpoint = string.IsNullOrWhiteSpace(url) ? null : url,
                Model = resolvedModel
            });
        }

        private static void AddFoundryFromEnv(AiOptions opts)
        {
            // Same opt-in rule as Ollama — only register when an env var is set.
            var url = Environment.GetEnvironmentVariable("FOUNDRY_URL");
            var model = Environment.GetEnvironmentVariable("FOUNDRY_MODEL");
            if (string.IsNullOrWhiteSpace(url) && string.IsNullOrWhiteSpace(model)) return;
            if (HasEngineOfKind(opts, "foundry")) return;

            var resolvedModel = string.IsNullOrWhiteSpace(model) ? "phi-4-mini-instruct" : model;
            opts.Engines.Add(new AiEngineCfg
            {
                Id = "foundry-env",
                Kind = "foundry",
                Label = "Foundry · " + resolvedModel,
                Endpoint = string.IsNullOrWhiteSpace(url) ? null : url,
                Model = resolvedModel
            });
        }

        private static bool HasEngineOfKind(AiOptions opts, string kind)
        {
            foreach (var e in opts.Engines)
            {
                if (e == null) continue;
                if (string.Equals(e.Kind, kind, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }
}
