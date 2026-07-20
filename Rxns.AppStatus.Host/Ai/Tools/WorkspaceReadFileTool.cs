using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rxns.Ai;
using Rxns.AppStatus.Host.Ai.Workspace;

namespace Rxns.AppStatus.Host.Ai.Tools
{
    /// <summary>
    /// Tool: read a single file under the configured workspace roots. Hard
    /// size cap so the model can't pull a 100MB log into a single tool
    /// response and torch the prompt window.
    /// </summary>
    public class WorkspaceReadFileTool : IAiToolHandler
    {
        public const int MaxBytes = 64 * 1024; // 64KB per call — multi-file reads happen via repeated calls

        private readonly AiOptions _options;
        public WorkspaceReadFileTool(AiOptions options) { _options = options ?? new AiOptions(); }

        public AiToolDefinition Definition { get; } = new AiToolDefinition
        {
            Name = "workspace_read_file",
            Description = "Read a file from the configured workspace root(s). Path must be inside one of the roots. Hard-capped at 64KB per call.",
            RequiresWriteAccess = false,
            InputSchemaJson = @"{
  ""type"": ""object"",
  ""properties"": {
    ""path"": { ""type"": ""string"", ""description"": ""Absolute filesystem path under a configured workspace root (e.g. 'C:/src/myrepo/README.md')."" }
  },
  ""required"": [""path""]
}"
        };

        public Task<AiToolResult> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
        {
            try
            {
                var args = string.IsNullOrWhiteSpace(argumentsJson) ? new JObject() : JObject.Parse(argumentsJson);
                var rawPath = (string)args["path"];
                var resolved = WorkspacePathGuard.TryResolve(rawPath, _options.WorkspaceRoots, out var err);
                if (resolved == null) return Task.FromResult(new AiToolResult { IsError = true, ErrorMessage = err });

                var fi = new FileInfo(resolved);
                if (!fi.Exists)
                    return Task.FromResult(new AiToolResult { IsError = true, ErrorMessage = "file not found: " + resolved });

                string content;
                bool truncated = false;
                if (fi.Length > MaxBytes)
                {
                    using var fs = File.OpenRead(resolved);
                    var buf = new byte[MaxBytes];
                    var read = fs.Read(buf, 0, MaxBytes);
                    content = System.Text.Encoding.UTF8.GetString(buf, 0, read);
                    truncated = true;
                }
                else
                {
                    content = File.ReadAllText(resolved);
                }

                return Task.FromResult(new AiToolResult
                {
                    OutputJson = JsonConvert.SerializeObject(new
                    {
                        path = resolved,
                        sizeBytes = fi.Length,
                        truncated = truncated,
                        truncatedAfterBytes = truncated ? (int?)MaxBytes : null,
                        content = content
                    })
                });
            }
            catch (Exception ex)
            {
                return Task.FromResult(new AiToolResult { IsError = true, ErrorMessage = ex.Message });
            }
        }
    }
}
