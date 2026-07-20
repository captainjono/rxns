using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rxns.Ai;
using Rxns.Health.AppStatus;

namespace Rxns.AppStatus.Host.Ai.Tools
{
    /// <summary>
    /// Read-only tool: ask the model to query the local AppStatus log buffer.
    /// Maps directly to <see cref="IAppStatusLogReader.GetLog"/>.
    /// </summary>
    public class QueryLogsTool : IAiToolHandler
    {
        private readonly IAppStatusLogReader _reader;

        public QueryLogsTool(IAppStatusLogReader reader)
        {
            _reader = reader;
        }

        public AiToolDefinition Definition { get; } = new AiToolDefinition
        {
            Name = "query_logs",
            Description = "Read recent log entries from a registered app. Use when the user asks 'what's happening with <app>' or wants to investigate a specific error.",
            RequiresWriteAccess = false,
            InputSchemaJson = @"{
  ""type"": ""object"",
  ""properties"": {
    ""systemName"": { ""type"": ""string"", ""description"": ""App SystemName, e.g. 'myapp'. Optional — omit to search across all apps."" },
    ""level"":      { ""type"": ""string"", ""enum"": [""Error"",""Warning"",""Information"",""Verbose""], ""description"": ""Filter by level."" },
    ""take"":       { ""type"": ""integer"", ""description"": ""Max rows (default 50, hard cap 200)."" }
  }
}"
        };

        public Task<AiToolResult> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
        {
            try
            {
                var args = string.IsNullOrWhiteSpace(argumentsJson) ? new JObject() : JObject.Parse(argumentsJson);
                string sys = (string)args["systemName"];
                string level = (string)args["level"];
                int take = args["take"] != null ? (int)args["take"] : 50;
                if (take > 200) take = 200;

                var page = _reader.GetLog(systemName: sys, level: level, since: null, skip: 0, take: take);
                return Task.FromResult(new AiToolResult { OutputJson = JsonConvert.SerializeObject(page) });
            }
            catch (Exception ex)
            {
                return Task.FromResult(new AiToolResult { IsError = true, ErrorMessage = ex.Message });
            }
        }
    }

    /// <summary>Read-only tool: list every app currently publishing to AppStatus.</summary>
    public class ListSystemsTool : IAiToolHandler
    {
        private readonly IAppStatusLogReader _reader;
        public ListSystemsTool(IAppStatusLogReader reader) { _reader = reader; }

        public AiToolDefinition Definition { get; } = new AiToolDefinition
        {
            Name = "list_systems",
            Description = "List every app SystemName currently publishing to AppStatus. Use this first when the user mentions an app by name to confirm it's registered.",
            RequiresWriteAccess = false,
            InputSchemaJson = "{ \"type\": \"object\", \"properties\": {} }"
        };

        public Task<AiToolResult> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
        {
            try
            {
                var systems = _reader.GetRegisteredSystems();
                return Task.FromResult(new AiToolResult { OutputJson = JsonConvert.SerializeObject(systems) });
            }
            catch (Exception ex)
            {
                return Task.FromResult(new AiToolResult { IsError = true, ErrorMessage = ex.Message });
            }
        }
    }

    /// <summary>Read-only tool: get rolling error/warn/info counts for one app.</summary>
    public class GetStatsTool : IAiToolHandler
    {
        private readonly IAppStatusLogReader _reader;
        public GetStatsTool(IAppStatusLogReader reader) { _reader = reader; }

        public AiToolDefinition Definition { get; } = new AiToolDefinition
        {
            Name = "get_stats",
            Description = "Get error / warning / info counts over the last hour and 24h for one app. Cheap; call before pulling logs to see if there's anything worth looking at.",
            RequiresWriteAccess = false,
            InputSchemaJson = @"{
  ""type"": ""object"",
  ""properties"": {
    ""systemName"": { ""type"": ""string"", ""description"": ""App SystemName, e.g. 'myapp'."" }
  },
  ""required"": [""systemName""]
}"
        };

        public Task<AiToolResult> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
        {
            try
            {
                var args = JObject.Parse(argumentsJson ?? "{}");
                string sys = (string)args["systemName"];
                var stats = _reader.GetStats(sys);
                return Task.FromResult(new AiToolResult { OutputJson = JsonConvert.SerializeObject(stats) });
            }
            catch (Exception ex)
            {
                return Task.FromResult(new AiToolResult { IsError = true, ErrorMessage = ex.Message });
            }
        }
    }

    /// <summary>
    /// WRITE tool: publish an arbitrary Rxn event to the in-process bus. Hidden
    /// when the portal is in read-only mode. Exposed only when the operator has
    /// explicitly toggled "full access".
    /// </summary>
    public class PublishEventTool : IAiToolHandler
    {
        public AiToolDefinition Definition { get; } = new AiToolDefinition
        {
            Name = "publish_event",
            Description = "Publish a Rxn event to the in-process bus. Only call this when the user has explicitly asked you to trigger a domain action. Argument 'envelope' must be a full JSON object including the 'T' type discriminator.",
            RequiresWriteAccess = true,
            InputSchemaJson = @"{
  ""type"": ""object"",
  ""properties"": {
    ""envelope"": { ""type"": ""object"", ""description"": ""Full event JSON including a 'T' field with the .NET type name."" }
  },
  ""required"": [""envelope""]
}"
        };

        public Task<AiToolResult> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
        {
            return Task.FromResult(new AiToolResult
            {
                OutputJson = JsonConvert.SerializeObject(new
                {
                    status = "queued",
                    note = "publish_event is staged for review; the operator must click 'Run' in the portal to actually publish. No event has been emitted."
                })
            });
        }
    }
}
