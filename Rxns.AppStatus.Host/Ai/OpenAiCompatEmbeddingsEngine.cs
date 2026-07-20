using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rxns.Ai;

namespace Rxns.AppStatus.Host.Ai
{
    /// <summary>
    /// Shared embeddings transport for any backend that speaks the OpenAI
    /// <c>POST /v1/embeddings</c> shape — Ollama (since 0.4) and Foundry
    /// Local both qualify. Concrete subclasses just supply the default
    /// endpoint + availability probe.
    /// </summary>
    public abstract class OpenAiCompatEmbeddingsEngine : IAiEmbeddingsEngine
    {
        protected readonly AiEngineCfg Cfg;
        protected readonly HttpClient Http;

        protected OpenAiCompatEmbeddingsEngine(AiEngineCfg cfg)
        {
            Cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
            Http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };  // big batches take time
        }

        public string EngineId => Cfg.Id;
        public string ModelId  => Cfg.Model;
        public abstract string Kind { get; }
        public virtual string Label => string.IsNullOrWhiteSpace(Cfg.Label) ? (Kind + " · " + (Cfg.Model ?? "?")) : Cfg.Label;

        protected abstract string DefaultEndpoint { get; }
        public abstract bool IsAvailable { get; }
        protected string ResolvedEndpoint => (string.IsNullOrWhiteSpace(Cfg.Endpoint) ? DefaultEndpoint : Cfg.Endpoint).TrimEnd('/');

        public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
        {
            if (texts == null || texts.Count == 0) return Array.Empty<float[]>();

            var body = new JObject
            {
                ["model"] = ModelId,
                ["input"] = new JArray(texts.Select(t => (object)(t ?? "")).ToArray())
            };

            using var msg = new HttpRequestMessage(HttpMethod.Post, ResolvedEndpoint + "/v1/embeddings")
            {
                Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json")
            };
            if (!string.IsNullOrWhiteSpace(Cfg.ApiKey))
                msg.Headers.Add("Authorization", "Bearer " + Cfg.ApiKey);

            using var resp = await Http.SendAsync(msg, ct).ConfigureAwait(false);
            var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException(Kind + " embeddings API error " + (int)resp.StatusCode + ": " + Truncate(text, 500));

            var doc = JObject.Parse(text);
            var data = doc["data"] as JArray;
            if (data == null) throw new InvalidOperationException(Kind + " embeddings response missing data[]: " + Truncate(text, 200));

            var result = new List<float[]>(data.Count);
            foreach (var item in data)
            {
                var arr = item["embedding"] as JArray;
                if (arr == null) { result.Add(Array.Empty<float>()); continue; }
                var vec = new float[arr.Count];
                for (var i = 0; i < arr.Count; i++) vec[i] = (float)arr[i];
                result.Add(vec);
            }
            return result;
        }

        private static string Truncate(string s, int max) =>
            string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max) + "…";
    }

    /// <summary>Ollama embeddings via <c>POST /v1/embeddings</c>. Default
    /// endpoint <c>http://localhost:11434</c>; default model
    /// <c>nomic-embed-text</c>.</summary>
    public class OllamaEmbeddingsEngine : OpenAiCompatEmbeddingsEngine
    {
        public OllamaEmbeddingsEngine(AiEngineCfg cfg) : base(cfg) { }
        public override string Kind => "ollama";
        protected override string DefaultEndpoint => "http://localhost:11434";
        public override bool IsAvailable
        {
            get
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    using var req = new HttpRequestMessage(HttpMethod.Get, ResolvedEndpoint + "/api/tags");
                    using var resp = Http.SendAsync(req, cts.Token).GetAwaiter().GetResult();
                    return resp.IsSuccessStatusCode;
                }
                catch { return false; }
            }
        }
    }

    /// <summary>Foundry Local embeddings — same OpenAI-compat surface as
    /// the chat engine but on the embeddings endpoint.</summary>
    public class FoundryEmbeddingsEngine : OpenAiCompatEmbeddingsEngine
    {
        public FoundryEmbeddingsEngine(AiEngineCfg cfg) : base(cfg) { }
        public override string Kind => "foundry";
        protected override string DefaultEndpoint => "http://localhost:5273";
        public override bool IsAvailable
        {
            get
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    using var req = new HttpRequestMessage(HttpMethod.Get, ResolvedEndpoint + "/v1/models");
                    using var resp = Http.SendAsync(req, cts.Token).GetAwaiter().GetResult();
                    return resp.IsSuccessStatusCode;
                }
                catch { return false; }
            }
        }
    }
}
