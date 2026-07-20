using System;
using System.Threading;
using System.Threading.Tasks;
using Rxns.Ai;

namespace Rxns.AppStatus.Host.Ai
{
    /// <summary>
    /// PLACEHOLDER — designed wire, no live impl yet.
    ///
    /// Wraps the user's local <c>claude</c> CLI as a long-lived subprocess via
    /// <c>Rxn.Create(pathToProcess, args, onInfo, onError)</c>. Requests are
    /// written to stdin as JSONL; responses come off stdout in the same shape.
    /// Lets the portal route conversations through the operator's own CLI
    /// session (their billing, their context) instead of the server's API key.
    ///
    /// Implementation deferred until claude-cli stabilises a streaming JSONL
    /// invocation mode that can host multi-turn conversations.
    /// <see cref="IsAvailable"/> stays false until <see cref="AiEngineCfg.CliPath"/>
    /// points at a real binary AND that mode lands.
    /// </summary>
    public class ClaudeProcessAiEngine : IAiChatEngine
    {
        private readonly AiEngineCfg _cfg;

        public ClaudeProcessAiEngine(AiEngineCfg cfg)
        {
            _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
        }

        public string EngineId => _cfg.Id;
        public string Kind => "cli";
        public string Label => string.IsNullOrWhiteSpace(_cfg.Label) ? "Claude CLI" : _cfg.Label;
        public string ModelId => _cfg.Model;

        public bool IsAvailable => false;  // designed wire only

        public Task<AiChatResponse> CompleteAsync(AiChatRequest request, CancellationToken ct = default)
        {
            throw new NotImplementedException(
                "ClaudeProcessAiEngine is a designed wire only. Use a 'claude' kind engine (Anthropic API) " +
                "or 'ollama' / 'foundry' kinds for local models.");
        }

        public Task<System.Collections.Generic.IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default) =>
            Task.FromResult<System.Collections.Generic.IReadOnlyList<string>>(new System.Collections.Generic.List<string>());
    }
}
