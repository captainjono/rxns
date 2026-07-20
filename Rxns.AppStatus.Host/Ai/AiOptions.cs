using System.Collections.Generic;

namespace Rxns.AppStatus.Host.Ai
{
    /// <summary>
    /// Server-side AI integration config. One options object covers every engine
    /// the portal can talk to — Claude, Ollama, Foundry Local, future CLI. Loaded
    /// from <c>appstatus.config</c> (Ai section) with env-var overrides.
    /// </summary>
    public class AiOptions
    {
        /// <summary>Declared engines. Each entry becomes one selectable engine in
        /// the portal's engine picker. Multiple entries of the same Kind (e.g. a
        /// local Ollama and a remote one) are supported — Id disambiguates them.</summary>
        public List<AiEngineCfg> Engines { get; set; } = new List<AiEngineCfg>();

        /// <summary>Engine id used when the caller doesn't specify one. Empty →
        /// the first <c>Default = true</c> entry wins; failing that, the first
        /// available engine in the list.</summary>
        public string DefaultEngineId { get; set; }

        /// <summary>When true, only tools with <c>RequiresWriteAccess == false</c>
        /// are advertised to any engine. Read-only default keeps surprise damage
        /// off the table; flipping it on still requires the per-action confirm
        /// flow in the monitor pane.</summary>
        public bool ReadOnly { get; set; } = true;

        /// <summary>Tool-agnostic base system prompt. Tool-call instructions are
        /// appended at request time by <see cref="Rxns.AppStatus.Host.Ai.AiChatController"/>
        /// ONLY when the engine actually advertises tools. Without this split,
        /// small local models (e.g. qwen2.5-7b on Foundry NPU) read the "prefer
        /// calling the available tools" line, find no tools in the request,
        /// and hallucinate tool-call-shaped JSON into the response body.</summary>
        public string SystemPrompt { get; set; } =
            "You are a diagnostic assistant embedded in the rxns support portal. " +
            "When the user asks about errors or system health, summarise findings concisely from the available context. " +
            "Default to read-only investigation; never propose write actions unless the user has explicitly enabled them.";

        /// <summary>Sentence appended to <see cref="SystemPrompt"/> ONLY when
        /// tools are being advertised on the request. Keeps the tool-using
        /// instruction off the model's plate when there are no tools to use,
        /// which prevents the "Portland / )(({..}))" hallucination pattern.</summary>
        public string ToolsAppendix { get; set; } =
            "You have access to read-only tools (query_logs, infra_list_components, get_stats, query_appinsights, ...). " +
            "When the user asks about live state, prefer calling a tool over guessing. " +
            "Tool calls MUST use the OpenAI tool_calls field — never write JSON into your reply text.";

        /// <summary>Free-form project knowledge prepended to every system prompt.
        /// Think of it as a <c>CLAUDE.md</c> for the support portal: how to
        /// start things, common commands, naming conventions, where logs live,
        /// who runs what — anything a brand-new model wouldn't know about
        /// THIS repo. Edited via the bubble's Settings tab and persisted to
        /// <c>appstatus.local.config</c> under <c>Ai.ProjectContext</c>.</summary>
        public string ProjectContext { get; set; }

        /// <summary>Workspace roots the bubble can scan + read from. Multi-root
        /// because the user often has several related repositories checked out
        /// side-by-side. Used by both auto-discovery and (later) the
        /// JIT file tools + embedding RAG.</summary>
        public List<string> WorkspaceRoots { get; set; } = new List<string>();

        /// <summary>Discovery glob patterns evaluated against each workspace
        /// root. Empty list falls back to <see cref="Workspace.WorkspaceScanner.DefaultPatterns"/>.
        /// Standard cone-glob shape: leading <c>!</c> excludes, <c>**</c> recurses.</summary>
        public List<string> DiscoveryPatterns { get; set; } = new List<string>();

        /// <summary>Absolute paths of files the operator ticked for inclusion
        /// in every chat. Server reads these on every request and inlines them
        /// as a "Workspace knowledge" preamble before the user's message.</summary>
        public List<string> SelectedKnowledgeFiles { get; set; } = new List<string>();

        /// <summary>Max bytes of selected-knowledge content to inline per chat
        /// request. Files past the cap are listed by name but their bodies
        /// skipped — keeps a large auto-discovery selection from torching
        /// every model's context window.</summary>
        public int KnowledgeBudgetBytes { get; set; } = 64 * 1024;

        /// <summary>Embeddings engine declarations. Same shape as
        /// <see cref="Engines"/>; loaded into <c>IAiEmbeddingsEngine</c>
        /// impls by <c>AiModule</c>. Used by the per-root knowledge-index
        /// builder + the <c>search_workspace_knowledge</c> tool.</summary>
        public List<AiEngineCfg> EmbeddingsEngines { get; set; } = new List<AiEngineCfg>();

        /// <summary>Which embeddings engine the "Build index" button uses
        /// by default. Null/empty → first available embeddings engine.</summary>
        public string DefaultEmbeddingsEngineId { get; set; }
    }

    /// <summary>
    /// One engine declaration. The factory uses <see cref="Kind"/> to decide
    /// which engine class to instantiate; everything else is engine-specific
    /// config the engine reads off this record at construction time.
    /// </summary>
    public class AiEngineCfg
    {
        /// <summary>Stable engine id used on the wire (chat request `engine` field,
        /// UI selector). Must be unique within <see cref="AiOptions.Engines"/>.</summary>
        public string Id { get; set; }

        /// <summary>Engine family: <c>claude</c> | <c>ollama</c> | <c>foundry</c> |
        /// <c>cli</c>. Drives engine-class selection in the factory.</summary>
        public string Kind { get; set; }

        /// <summary>Human label for the UI ("Claude · Sonnet 4.6", "Ollama · llama3.1:8b").
        /// Falls back to <c>Kind + " · " + Model</c> when missing.</summary>
        public string Label { get; set; }

        /// <summary>Base URL for HTTP engines. Defaults:
        /// claude → <c>https://api.anthropic.com</c>, ollama → <c>http://localhost:11434</c>,
        /// foundry → <c>http://localhost:5273</c>. Set explicitly to point at a
        /// remote Ollama or a non-default Foundry port.</summary>
        public string Endpoint { get; set; }

        /// <summary>API key for the engine (Anthropic). Local engines (Ollama,
        /// Foundry) leave this empty.</summary>
        public string ApiKey { get; set; }

        /// <summary>Default model id. Per-request overrides via
        /// <see cref="Rxns.Ai.AiChatRequest.ModelOverride"/> are honored.</summary>
        public string Model { get; set; }

        /// <summary>Engine-specific system prompt. Empty → falls back to
        /// <see cref="AiOptions.SystemPrompt"/>.</summary>
        public string SystemPrompt { get; set; }

        /// <summary>When true, this engine is preferred when no explicit engine
        /// id is supplied on a request. First-default-wins.</summary>
        public bool Default { get; set; }

        /// <summary>CLI engines only — path to the executable.</summary>
        public string CliPath { get; set; }
    }
}
