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
    /// In-process Claude engine speaking the Anthropic Messages API directly
    /// over HTTP. One instance per <c>claude</c> entry in <see cref="AiOptions.Engines"/>.
    ///
    /// Avoids taking a heavy SDK dep on the portal host — the wire is small
    /// and stable. <see cref="IsAvailable"/> returns true once an API key
    /// is present in the engine's config entry.
    /// </summary>
    public class ClaudeApiAiEngine : IAiChatEngine
    {
        private const string ApiVersion = "2023-06-01";
        private const string DefaultEndpoint = "https://api.anthropic.com";
        private const string MessagesPath = "/v1/messages";

        private readonly AiEngineCfg _cfg;
        private readonly AiToolRegistry _tools;
        private readonly HttpClient _http;

        public ClaudeApiAiEngine(AiEngineCfg cfg, AiToolRegistry tools)
        {
            _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
            _tools = tools;
            _http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        }

        public string EngineId => _cfg.Id;
        public string Kind => "claude";
        public string Label => string.IsNullOrWhiteSpace(_cfg.Label) ? ("Claude · " + (_cfg.Model ?? "?")) : _cfg.Label;
        public string ModelId => _cfg.Model;
        public bool IsAvailable => !string.IsNullOrWhiteSpace(_cfg.ApiKey);

        public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default)
        {
            if (!IsAvailable) return new List<string>();

            // Anthropic exposes GET /v1/models (paginated; `data` is the page).
            // Cap to first page — there are <20 Claude models in the wild.
            var endpoint = (string.IsNullOrWhiteSpace(_cfg.Endpoint) ? DefaultEndpoint : _cfg.Endpoint.TrimEnd('/')) + "/v1/models?limit=50";

            try
            {
                using var msg = new HttpRequestMessage(HttpMethod.Get, endpoint);
                msg.Headers.Add("x-api-key", _cfg.ApiKey);
                msg.Headers.Add("anthropic-version", ApiVersion);
                using var resp = await _http.SendAsync(msg, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return FallbackKnownModels();
                var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                var doc = JObject.Parse(text);
                var list = new List<string>();
                foreach (var item in doc["data"] as JArray ?? new JArray())
                {
                    var id = (string)item["id"];
                    if (!string.IsNullOrWhiteSpace(id)) list.Add(id);
                }
                return list.Count == 0 ? FallbackKnownModels() : list;
            }
            catch
            {
                return FallbackKnownModels();
            }
        }

        // Fallback when /v1/models fails — keeps the picker populated with the
        // public Claude 4.x family so the user can still choose.
        private static IReadOnlyList<string> FallbackKnownModels() => new List<string>
        {
            "claude-opus-4-7",
            "claude-opus-4-6",
            "claude-sonnet-4-6",
            "claude-sonnet-4-5",
            "claude-haiku-4-5",
            "claude-3-7-sonnet-latest"
        };

        public async Task<AiChatResponse> CompleteAsync(AiChatRequest request, CancellationToken ct = default)
        {
            if (!IsAvailable) throw new InvalidOperationException("ClaudeApiAiEngine '" + EngineId + "': ApiKey not configured.");
            if (request == null) throw new ArgumentNullException(nameof(request));

            var body = BuildRequestBody(request);
            var endpoint = (string.IsNullOrWhiteSpace(_cfg.Endpoint) ? DefaultEndpoint : _cfg.Endpoint.TrimEnd('/')) + MessagesPath;

            using var msg = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            msg.Headers.Add("x-api-key", _cfg.ApiKey);
            msg.Headers.Add("anthropic-version", ApiVersion);

            using var resp = await _http.SendAsync(msg, ct).ConfigureAwait(false);
            var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                return new AiChatResponse
                {
                    EngineId = EngineId,
                    ModelId = request.ModelOverride ?? ModelId,
                    AssistantText = "Claude API error: " + (int)resp.StatusCode + " " + text,
                    StopReason = "error"
                };
            }

            return ParseResponse(text, request);
        }

        private string BuildRequestBody(AiChatRequest request)
        {
            var anthropicMessages = new JArray();
            foreach (var m in request.Messages ?? new List<AiChatMessage>())
            {
                if (m.Role == "tool")
                {
                    anthropicMessages.Add(new JObject
                    {
                        ["role"] = "user",
                        ["content"] = new JArray(new JObject
                        {
                            ["type"] = "tool_result",
                            ["tool_use_id"] = m.ToolUseId,
                            ["content"] = m.Content
                        })
                    });
                }
                else
                {
                    anthropicMessages.Add(new JObject
                    {
                        ["role"] = m.Role,
                        ["content"] = m.Content
                    });
                }
            }

            var body = new JObject
            {
                ["model"] = request.ModelOverride ?? ModelId,
                ["max_tokens"] = request.MaxTokens,
                ["system"] = string.IsNullOrEmpty(request.SystemPrompt) ? _cfg.SystemPrompt : request.SystemPrompt,
                ["messages"] = anthropicMessages
            };

            if (request.AllowToolCalls && _tools != null)
            {
                var allTools = _tools.List();
                if (request.AllowedToolNames != null)
                    allTools = allTools.Where(t => request.AllowedToolNames.Contains(t.Definition.Name)).ToList();

                if (allTools.Count > 0)
                {
                    var toolsArr = new JArray();
                    foreach (var t in allTools)
                    {
                        toolsArr.Add(new JObject
                        {
                            ["name"] = t.Definition.Name,
                            ["description"] = t.Definition.Description,
                            ["input_schema"] = JToken.Parse(t.Definition.InputSchemaJson ?? "{\"type\":\"object\"}")
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
                resp.StopReason = (string)doc["stop_reason"];
                resp.InputTokens = (int?)doc["usage"]?["input_tokens"];
                resp.OutputTokens = (int?)doc["usage"]?["output_tokens"];

                var sb = new StringBuilder();
                foreach (var block in doc["content"] as JArray ?? new JArray())
                {
                    var type = (string)block["type"];
                    if (type == "text")
                    {
                        sb.Append((string)block["text"]);
                    }
                    else if (type == "tool_use")
                    {
                        resp.ToolCalls.Add(new AiToolCall
                        {
                            Id = (string)block["id"],
                            Name = (string)block["name"],
                            ArgumentsJson = block["input"]?.ToString(Formatting.None) ?? "{}"
                        });
                    }
                }
                resp.AssistantText = sb.ToString();
            }
            catch (Exception ex)
            {
                resp.AssistantText = "Failed to parse Claude response: " + ex.Message + "\n\nRaw: " + responseJson;
                resp.StopReason = "error";
            }

            return resp;
        }
    }
}
