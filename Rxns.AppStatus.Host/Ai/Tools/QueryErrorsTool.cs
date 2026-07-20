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
    /// Read-only tool — shortcut over <see cref="IAppStatusLogReader.GetErrors"/>.
    /// Use when the user asks "what errors am I seeing", "find recent failures", etc.
    /// Faster than query_logs with level=Error because it also pulls historical
    /// errors from <c>IAppErrorManager.GetOutstandingErrors</c>, not just the
    /// in-memory log buffer.
    /// </summary>
    public class QueryErrorsTool : IAiToolHandler
    {
        private readonly IAppStatusLogReader _reader;

        public QueryErrorsTool(IAppStatusLogReader reader)
        {
            _reader = reader;
        }

        public AiToolDefinition Definition { get; } = new AiToolDefinition
        {
            Name = "query_errors",
            Description = "Pull recent errors for one app — merges live log entries (Level=Error/Fatal) with historical entries from the AppErrorManager. Use when the user asks about errors, failures, or crashes.",
            RequiresWriteAccess = false,
            InputSchemaJson = @"{
  ""type"": ""object"",
  ""properties"": {
    ""systemName"": { ""type"": ""string"", ""description"": ""App SystemName (e.g. 'myapp'). Omit to get errors across all apps."" },
    ""since"":      { ""type"": ""string"", ""description"": ""ISO timestamp; only entries at or after this time. Optional."" },
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
                DateTime? since = null;
                if (args["since"] != null && DateTime.TryParse((string)args["since"], out var s)) since = s;
                int take = args["take"] != null ? (int)args["take"] : 50;
                if (take > 200) take = 200;

                var page = _reader.GetErrors(systemName: sys, since: since, skip: 0, take: take);
                return Task.FromResult(new AiToolResult { OutputJson = JsonConvert.SerializeObject(page) });
            }
            catch (Exception ex)
            {
                return Task.FromResult(new AiToolResult { IsError = true, ErrorMessage = ex.Message });
            }
        }
    }
}
