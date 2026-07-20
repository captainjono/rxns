using System.Threading;
using System.Threading.Tasks;

namespace Rxns.Ai
{
    public class AiToolDefinition
    {
        public string Name { get; set; }
        public string Description { get; set; }

        /// <summary>JSON-Schema description of the tool's input. The same shape
        /// Anthropic Messages API takes (<c>input_schema</c>). OpenAI-compat
        /// engines re-wrap this into their <c>parameters</c> field inside
        /// <c>tools[].function</c> — done by <c>OpenAiCompatAiEngine</c>.</summary>
        public string InputSchemaJson { get; set; }

        /// <summary>Hidden from the model when the host has the read-only flag on.
        /// Set true for state-changing tools (restart service, scale app, publish
        /// event, run shell command, …).</summary>
        public bool RequiresWriteAccess { get; set; }
    }

    public class AiToolResult
    {
        /// <summary>Serialised back to the model as the tool_result content.</summary>
        public string OutputJson { get; set; }
        public bool IsError { get; set; }
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// Implementations register one tool the model can call. Read-only handlers
    /// (e.g. "query_logs", "get_stats") set <c>RequiresWriteAccess = false</c>;
    /// state-changing ones ("publish_event", "scale_app", "restart_component")
    /// set it to <c>true</c> so the host can hide them when the user has
    /// the read-only toggle on.
    /// </summary>
    public interface IAiToolHandler
    {
        AiToolDefinition Definition { get; }
        Task<AiToolResult> ExecuteAsync(string argumentsJson, CancellationToken ct = default);
    }
}
