using System;
using System.Net.Http;
using System.Threading;

namespace Rxns.AppStatus.Host.Ai
{
    /// <summary>
    /// Local-first AI engine talking to an Ollama server (default <c>localhost:11434</c>).
    /// Uses Ollama's OpenAI-compat surface at <c>/v1/chat/completions</c> — supported
    /// since Ollama 0.4 — which gives proper <c>tool_calls</c> shape for free.
    ///
    /// <para>Availability is probed via <c>GET /api/tags</c>; result is cached for
    /// 30s so the engine selector stays responsive without hammering the local
    /// server.</para>
    ///
    /// <para>Cost: zero per token (local compute). Tool-calling quality depends
    /// on the model — qwen2.5, llama3.1 8B+, mistral-nemo work well; smaller
    /// models drop the tool_calls structure on tougher prompts.</para>
    /// </summary>
    public class OllamaAiEngine : OpenAiCompatAiEngine
    {
        public OllamaAiEngine(AiEngineCfg cfg, AiToolRegistry tools) : base(cfg, tools) { }

        public override string Kind => "ollama";
        protected override string DefaultEndpoint => "http://localhost:11434";

        private DateTime _lastProbeAt = DateTime.MinValue;
        private bool _lastProbeResult;
        private static readonly TimeSpan ProbeTtl = TimeSpan.FromSeconds(30);
        private readonly object _probeLock = new object();

        public override bool IsAvailable
        {
            get
            {
                lock (_probeLock)
                {
                    if (DateTime.UtcNow - _lastProbeAt < ProbeTtl) return _lastProbeResult;
                    _lastProbeAt = DateTime.UtcNow;
                    _lastProbeResult = Probe();
                    return _lastProbeResult;
                }
            }
        }

        private bool Probe()
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                using var req = new HttpRequestMessage(HttpMethod.Get, ResolvedEndpoint + "/api/tags");
                using var resp = Http.SendAsync(req, cts.Token).GetAwaiter().GetResult();
                return resp.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
    }
}
