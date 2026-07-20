using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Rxns.Ai
{
    /// <summary>
    /// Single conversational turn the engine receives. Engines render the history
    /// into whatever shape the underlying transport wants — Anthropic Messages,
    /// OpenAI-compatible (Ollama, Foundry Local), or a long-lived CLI subprocess —
    /// callers just append messages here.
    /// </summary>
    public class AiChatMessage
    {
        public string Role { get; set; }      // "user" | "assistant" | "tool"
        public string Content { get; set; }
        public string ToolName { get; set; }  // when Role == "tool"
        public string ToolUseId { get; set; } // when Role == "tool" (echo of the tool_use id)
    }

    public class AiChatRequest
    {
        public IList<AiChatMessage> Messages { get; set; }
        public string SystemPrompt { get; set; }
        public int MaxTokens { get; set; } = 4096;
        public bool AllowToolCalls { get; set; } = true;
        public IReadOnlyCollection<string> AllowedToolNames { get; set; } // null = all available

        /// <summary>Optional override for the engine's configured model id. Lets the
        /// UI pin a model per chat (e.g. "qwen2.5-coder:7b") without re-registering
        /// the engine.</summary>
        public string ModelOverride { get; set; }
    }

    public class AiToolCall
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string ArgumentsJson { get; set; }
    }

    public class AiChatResponse
    {
        public string AssistantText { get; set; }
        public IList<AiToolCall> ToolCalls { get; set; }
        public string StopReason { get; set; }   // "end_turn" | "tool_use" | "max_tokens" | "error"
        public string ModelId { get; set; }
        public string EngineId { get; set; }
        public int? InputTokens { get; set; }
        public int? OutputTokens { get; set; }
    }

    /// <summary>
    /// Transport-agnostic chat surface for any AI engine the portal can route to.
    /// Implementations register themselves in DI; <c>AiChatEngineFactory</c>
    /// collects them and picks one by <see cref="EngineId"/>.
    ///
    /// <para>Built-in impls (in <c>Rxns.AppStatus.Host.Ai</c>):</para>
    /// <list type="bullet">
    /// <item><c>ClaudeApiAiEngine</c> — Anthropic Messages API over HTTP.</item>
    /// <item><c>OllamaAiEngine</c> — OpenAI-compat REST at <c>localhost:11434/v1</c>.</item>
    /// <item><c>FoundryAiEngine</c> — Foundry Local OpenAI-compat endpoint.</item>
    /// <item><c>ClaudeProcessAiEngine</c> — long-lived <c>claude</c> CLI subprocess (designed wire).</item>
    /// </list>
    ///
    /// Augmentation modules (e.g. a consumer app's support portal) can
    /// register their own <see cref="IAiChatEngine"/> impls
    /// the same way they register <see cref="IAiToolHandler"/> tools today.
    /// </summary>
    public interface IAiChatEngine
    {
        /// <summary>Stable identifier used to address this engine over the wire
        /// (e.g. "claude-sdk", "ollama-llama31", "foundry-phi"). Comes from the
        /// engine's config entry — multiple instances of the same engine kind
        /// pointing at different endpoints are supported.</summary>
        string EngineId { get; }

        /// <summary>Human-readable label shown in the engine selector
        /// ("Claude · Sonnet 4.6", "Ollama · llama3.1:8b", …).</summary>
        string Label { get; }

        /// <summary>Engine family: "claude" | "ollama" | "foundry" | "cli" | …
        /// Used by the UI to group selectors and decide rough cost.</summary>
        string Kind { get; }

        /// <summary>Default model id this engine will use when the request
        /// doesn't override it.</summary>
        string ModelId { get; }

        /// <summary>True when the engine has everything it needs to serve a
        /// request (api key set, local server reachable, etc.). Probed cheaply;
        /// the factory falls through to the next engine if false.</summary>
        bool IsAvailable { get; }

        Task<AiChatResponse> CompleteAsync(AiChatRequest request, CancellationToken ct = default);

        /// <summary>List the models this engine can route to. Used by the UI's
        /// per-engine model picker so the operator can pick e.g.
        /// <c>qwen2.5-coder:7b</c> on Ollama or <c>claude-sonnet-4-6</c> on Claude
        /// without editing config.
        ///
        /// <para>Implementations:</para>
        /// <list type="bullet">
        /// <item><c>ClaudeApiAiEngine</c> hits Anthropic's <c>GET /v1/models</c>.</item>
        /// <item><c>OpenAiCompatAiEngine</c> hits <c>GET /v1/models</c> on the local server.</item>
        /// <item>Engines that can't list models return an empty list — the UI falls
        /// back to the engine's <see cref="ModelId"/> as the only choice.</item>
        /// </list>
        /// </summary>
        Task<System.Collections.Generic.IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default);
    }
}
