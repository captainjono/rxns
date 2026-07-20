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
    /// Shared transport for OpenAI-compatible chat APIs. Both Ollama
    /// (<c>localhost:11434/v1/chat/completions</c>) and Foundry Local speak this
    /// wire format, so this base class owns the request/response shape and
    /// concrete engines just provide the endpoint + availability probe.
    ///
    /// <para>Translation between the portal's Anthropic-flavoured shape and the
    /// OpenAI shape:</para>
    /// <list type="bullet">
    /// <item><c>tool</c> role → <c>role: "tool"</c> with <c>tool_call_id</c></item>
    /// <item><c>AiToolDefinition</c> → <c>tools[i].function {name, description, parameters}</c></item>
    /// <item><c>tool_calls[]</c> on the response → <c>AiToolCall[]</c></item>
    /// <item><c>finish_reason: "tool_calls"</c> → <c>StopReason: "tool_use"</c>
    /// (so <see cref="AiChatController"/>'s round-trip logic stays engine-agnostic)</item>
    /// </list>
    /// </summary>
    public abstract class OpenAiCompatAiEngine : IAiChatEngine
    {
        protected readonly AiEngineCfg Cfg;
        protected readonly AiToolRegistry Tools;
        protected readonly HttpClient Http;

        protected OpenAiCompatAiEngine(AiEngineCfg cfg, AiToolRegistry tools)
        {
            Cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
            Tools = tools;
            Http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        }

        public string EngineId => Cfg.Id;
        public abstract string Kind { get; }
        public virtual string Label => string.IsNullOrWhiteSpace(Cfg.Label) ? (Kind + " · " + (Cfg.Model ?? "?")) : Cfg.Label;
        public string ModelId => Cfg.Model;

        /// <summary>Default endpoint base when the engine cfg entry doesn't pin one.</summary>
        protected abstract string DefaultEndpoint { get; }

        /// <summary>Probe the local server. Implementations override with the cheapest
        /// reachability check their backend offers (Ollama → <c>GET /api/tags</c>,
        /// Foundry → <c>GET /v1/models</c>). The base impl just checks the cfg has an
        /// endpoint resolvable — subclasses replace this with a real probe once they
        /// have one to call. Result is cached for the lifetime of the engine.</summary>
        public abstract bool IsAvailable { get; }

        protected string ResolvedEndpoint => (string.IsNullOrWhiteSpace(Cfg.Endpoint) ? DefaultEndpoint : Cfg.Endpoint).TrimEnd('/');

        public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default)
        {
            // OpenAI-compat servers expose GET /v1/models. Foundry uses the same
            // shape; Ollama added it in 0.4+. Hit it cheap and tolerate failures.
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(3));
                using var req = new HttpRequestMessage(HttpMethod.Get, ResolvedEndpoint + "/v1/models");
                if (!string.IsNullOrWhiteSpace(Cfg.ApiKey))
                    req.Headers.Add("Authorization", "Bearer " + Cfg.ApiKey);
                using var resp = await Http.SendAsync(req, cts.Token).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return new List<string>();
                var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                var doc = JObject.Parse(text);
                var list = new List<string>();
                foreach (var item in doc["data"] as JArray ?? new JArray())
                {
                    var id = (string)item["id"];
                    if (!string.IsNullOrWhiteSpace(id)) list.Add(id);
                }
                return list;
            }
            catch
            {
                return new List<string>();
            }
        }

        public async Task<AiChatResponse> CompleteAsync(AiChatRequest request, CancellationToken ct = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            var body = BuildRequestBody(request);
            var url = ResolvedEndpoint + "/v1/chat/completions";

            using var msg = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            if (!string.IsNullOrWhiteSpace(Cfg.ApiKey))
                msg.Headers.Add("Authorization", "Bearer " + Cfg.ApiKey);

            using var resp = await Http.SendAsync(msg, ct).ConfigureAwait(false);
            var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                return new AiChatResponse
                {
                    EngineId = EngineId,
                    ModelId = request.ModelOverride ?? ModelId,
                    AssistantText = Kind + " API error: " + (int)resp.StatusCode + " " + text,
                    StopReason = "error"
                };
            }
            return ParseResponse(text, request);
        }

        private string BuildRequestBody(AiChatRequest request)
        {
            var openAiMessages = new JArray();

            // OpenAI wants a "system" message rather than a top-level "system" field.
            var systemPrompt = string.IsNullOrEmpty(request.SystemPrompt) ? Cfg.SystemPrompt : request.SystemPrompt;
            if (!string.IsNullOrWhiteSpace(systemPrompt))
                openAiMessages.Add(new JObject { ["role"] = "system", ["content"] = systemPrompt });

            foreach (var m in request.Messages ?? new List<AiChatMessage>())
            {
                if (m.Role == "tool")
                {
                    openAiMessages.Add(new JObject
                    {
                        ["role"] = "tool",
                        ["tool_call_id"] = m.ToolUseId,
                        ["name"] = m.ToolName,
                        ["content"] = m.Content
                    });
                }
                else if (m.Role == "assistant" && string.IsNullOrEmpty(m.Content))
                {
                    // Skip — an empty assistant turn would confuse OpenAI compat servers.
                    continue;
                }
                else
                {
                    openAiMessages.Add(new JObject
                    {
                        ["role"] = m.Role,
                        ["content"] = m.Content ?? string.Empty
                    });
                }
            }

            var body = new JObject
            {
                ["model"] = request.ModelOverride ?? ModelId,
                ["messages"] = openAiMessages,
                ["max_tokens"] = request.MaxTokens,
                ["stream"] = false
            };

            if (request.AllowToolCalls && Tools != null)
            {
                var allTools = Tools.List();
                if (request.AllowedToolNames != null)
                    allTools = allTools.Where(t => request.AllowedToolNames.Contains(t.Definition.Name)).ToList();

                if (allTools.Count > 0)
                {
                    var toolsArr = new JArray();
                    foreach (var t in allTools)
                    {
                        toolsArr.Add(new JObject
                        {
                            ["type"] = "function",
                            ["function"] = new JObject
                            {
                                ["name"] = t.Definition.Name,
                                ["description"] = t.Definition.Description,
                                ["parameters"] = JToken.Parse(t.Definition.InputSchemaJson ?? "{\"type\":\"object\"}")
                            }
                        });
                    }
                    body["tools"] = toolsArr;
                }
            }

            return body.ToString(Formatting.None);
        }

        private AiChatResponse ParseResponse(string responseJson, AiChatRequest request)
        {
            var resp = new AiChatResponse
            {
                EngineId = EngineId,
                ToolCalls = new List<AiToolCall>(),
                ModelId = request.ModelOverride ?? ModelId
            };

            try
            {
                var doc = JObject.Parse(responseJson);
                resp.InputTokens = (int?)doc["usage"]?["prompt_tokens"];
                resp.OutputTokens = (int?)doc["usage"]?["completion_tokens"];

                var choice = (doc["choices"] as JArray)?.FirstOrDefault();
                if (choice == null)
                {
                    resp.AssistantText = "(no choices in response)";
                    resp.StopReason = "error";
                    return resp;
                }

                var finishReason = (string)choice["finish_reason"];
                // Map OpenAI's "tool_calls" to the portal's canonical "tool_use" so
                // AiChatController's round-trip logic stays engine-agnostic.
                resp.StopReason = finishReason == "tool_calls" ? "tool_use" : (finishReason ?? "end_turn");

                var message = choice["message"];
                resp.AssistantText = (string)message?["content"] ?? string.Empty;

                var toolCalls = message?["tool_calls"] as JArray;
                if (toolCalls != null)
                {
                    foreach (var call in toolCalls)
                    {
                        var fn = call["function"];
                        resp.ToolCalls.Add(new AiToolCall
                        {
                            Id = (string)call["id"],
                            Name = (string)fn?["name"],
                            ArgumentsJson = (string)fn?["arguments"] ?? "{}"
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                resp.AssistantText = "Failed to parse " + Kind + " response: " + ex.Message + "\n\nRaw: " + responseJson;
                resp.StopReason = "error";
            }

            return resp;
        }
    }
}
