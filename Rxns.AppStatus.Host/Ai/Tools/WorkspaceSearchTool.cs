using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.FileSystemGlobbing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rxns.Ai;
using Rxns.AppStatus.Host.Ai.Workspace;

namespace Rxns.AppStatus.Host.Ai.Tools
{
    /// <summary>
    /// Tool: grep-style search across configured workspace roots. Returns
    /// file:line:snippet matches. Use when the model wants to find every
    /// occurrence of a symbol/string without first listing then reading.
    /// </summary>
    public class WorkspaceSearchTool : IAiToolHandler
    {
        public const int MaxResults = 200;
        public const int MaxFilesScanned = 5000;
        public const long MaxFileBytes = 1 * 1024 * 1024; // 1MB — skip larger files

        private readonly AiOptions _options;
        public WorkspaceSearchTool(AiOptions options) { _options = options ?? new AiOptions(); }

        public AiToolDefinition Definition { get; } = new AiToolDefinition
        {
            Name = "workspace_search",
            Description = "Grep-style text search across configured workspace roots. Returns file path + line number + matched line for up to 200 matches. Use when looking for symbol usages, strings, or specific config keys.",
            RequiresWriteAccess = false,
            InputSchemaJson = @"{
  ""type"": ""object"",
  ""properties"": {
    ""query"":     { ""type"": ""string"", ""description"": ""Search text. Literal by default; set regex=true for a .NET regex."" },
    ""regex"":     { ""type"": ""boolean"", ""description"": ""Treat query as a regular expression (default false)."" },
    ""pattern"":   { ""type"": ""string"",  ""description"": ""File glob to limit scan scope (default '**/*'). Use '**/*.cs' for C# only, '**/*.md' for docs."" },
    ""root"":      { ""type"": ""string"",  ""description"": ""Restrict to one configured root. Omit to search all."" },
    ""maxResults"":{ ""type"": ""integer"", ""description"": ""Max match rows to return (default 50, hard cap 200)."" }
  },
  ""required"": [""query""]
}"
        };

        public Task<AiToolResult> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
        {
            try
            {
                var args = string.IsNullOrWhiteSpace(argumentsJson) ? new JObject() : JObject.Parse(argumentsJson);
                var query = (string)args["query"];
                if (string.IsNullOrWhiteSpace(query))
                    return Task.FromResult(new AiToolResult { IsError = true, ErrorMessage = "query is required" });

                var isRegex = (bool?)args["regex"] ?? false;
                var pattern = (string)args["pattern"] ?? "**/*";
                var rootArg = (string)args["root"];
                var maxResults = (int?)args["maxResults"] ?? 50;
                if (maxResults < 1) maxResults = 50;
                if (maxResults > MaxResults) maxResults = MaxResults;

                List<string> rootsToScan;
                if (string.IsNullOrWhiteSpace(rootArg))
                {
                    rootsToScan = (_options.WorkspaceRoots ?? new List<string>())
                        .Where(r => !string.IsNullOrWhiteSpace(r)).ToList();
                    if (rootsToScan.Count == 0)
                        return Task.FromResult(new AiToolResult { IsError = true, ErrorMessage = "no workspace roots configured" });
                }
                else
                {
                    var resolved = WorkspacePathGuard.TryResolve(rootArg, _options.WorkspaceRoots, out var err);
                    if (resolved == null) return Task.FromResult(new AiToolResult { IsError = true, ErrorMessage = err });
                    rootsToScan = new List<string> { resolved };
                }

                Regex regex;
                try
                {
                    regex = isRegex
                        ? new Regex(query, RegexOptions.Compiled | RegexOptions.IgnoreCase)
                        : new Regex(Regex.Escape(query), RegexOptions.Compiled | RegexOptions.IgnoreCase);
                }
                catch (Exception ex)
                {
                    return Task.FromResult(new AiToolResult { IsError = true, ErrorMessage = "bad regex: " + ex.Message });
                }

                var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
                foreach (var p in (pattern ?? "**/*").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0))
                {
                    if (p.StartsWith("!")) matcher.AddExclude(p.Substring(1));
                    else matcher.AddInclude(p);
                }

                var matches = new List<object>();
                var filesScanned = 0;
                var capped = false;

                foreach (var root in rootsToScan)
                {
                    if (!Directory.Exists(root)) continue;
                    var res = matcher.Execute(new Microsoft.Extensions.FileSystemGlobbing.Abstractions.DirectoryInfoWrapper(new DirectoryInfo(root)));

                    foreach (var fileMatch in res.Files)
                    {
                        if (matches.Count >= maxResults) { capped = true; break; }
                        if (filesScanned >= MaxFilesScanned) { capped = true; break; }
                        filesScanned++;

                        var abs = Path.GetFullPath(Path.Combine(root, fileMatch.Path));
                        FileInfo fi;
                        try { fi = new FileInfo(abs); } catch { continue; }
                        if (!fi.Exists || fi.Length > MaxFileBytes) continue;

                        string[] lines;
                        try { lines = File.ReadAllLines(abs); } catch { continue; }
                        for (var i = 0; i < lines.Length; i++)
                        {
                            if (matches.Count >= maxResults) { capped = true; break; }
                            if (regex.IsMatch(lines[i]))
                            {
                                var snippet = lines[i];
                                if (snippet.Length > 300) snippet = snippet.Substring(0, 300) + "…";
                                matches.Add(new
                                {
                                    root = root,
                                    path = abs,
                                    relativePath = fileMatch.Path.Replace('\\', '/'),
                                    line = i + 1,
                                    text = snippet
                                });
                            }
                        }
                    }
                    if (matches.Count >= maxResults) break;
                }

                return Task.FromResult(new AiToolResult
                {
                    OutputJson = JsonConvert.SerializeObject(new
                    {
                        count = matches.Count,
                        filesScanned = filesScanned,
                        capped = capped,
                        matches = matches
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
