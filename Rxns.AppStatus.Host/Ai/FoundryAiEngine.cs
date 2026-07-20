using System;
using System.Net.Http;
using System.Threading;

namespace Rxns.AppStatus.Host.Ai
{
    /// <summary>
    /// Local-first AI engine talking to Foundry Local's OpenAI-compatible REST
    /// surface (default <c>http://localhost:5273/v1</c>; configurable via the
    /// <c>foundry service status</c> printout).
    ///
    /// <para>Foundry exposes models loaded into the local runtime via
    /// <c>GET /v1/models</c> (same OpenAI shape); availability probe hits that.
    /// Tool-calling quality depends on the deployed model — Phi-4-mini and
    /// Llama 3.1 builds advertised by Foundry support function-calling.</para>
    /// </summary>
    public class FoundryAiEngine : OpenAiCompatAiEngine
    {
        public FoundryAiEngine(AiEngineCfg cfg, AiToolRegistry tools) : base(cfg, tools) { }

        public override string Kind => "foundry";
        protected override string DefaultEndpoint => "http://localhost:5273";

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
                using var req = new HttpRequestMessage(HttpMethod.Get, ResolvedEndpoint + "/v1/models");
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
