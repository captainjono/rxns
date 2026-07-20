using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.FileSystemGlobbing;
using Rxns.Logging;

namespace Rxns.AppStatus.Host.Ai.Workspace
{
    /// <summary>
    /// One discovered candidate file inside a configured workspace root.
    /// Surfaced to the UI so the operator can tick which files should
    /// auto-attach to every chat's system prompt.
    /// </summary>
    public class WorkspaceFileInfo
    {
        public string Root { get; set; }            // workspace root the file belongs to
        public string RelativePath { get; set; }    // path relative to Root, forward-slashed
        public string AbsolutePath { get; set; }    // full filesystem path
        public long   SizeBytes   { get; set; }
        public DateTime ModifiedUtc { get; set; }
    }

    /// <summary>
    /// Walks each configured workspace root and returns files matching the
    /// configured discovery patterns. The operator picks from these via the
    /// bubble's Settings tab; selection persists to <c>appstatus.local.config</c>.
    ///
    /// <para>Patterns are evaluated by <see cref="Matcher"/> from
    /// <c>Microsoft.Extensions.FileSystemGlobbing</c> — standard cone-glob
    /// shape (<c>**</c> for recursive, leading <c>!</c> for exclude). Defaults
    /// catch the rxns-flavoured knowledge-doc convention (CLAUDE.md, AGENTS.md,
    /// README.md, docs/**/*.md, plus *-guide.md anywhere).</para>
    /// </summary>
    public class WorkspaceScanner
    {
        public static readonly IReadOnlyList<string> DefaultPatterns = new[]
        {
            "CLAUDE.md",
            "AGENTS.md",
            "README.md",
            "docs/**/*.md",
            "**/*-guide.md"
        };

        // Hard caps — prevent runaway scans on a misconfigured root (e.g.
        // someone points us at C:\ by mistake).
        public const int MaxFilesPerRoot = 2000;
        public const long MaxIndividualFileBytes = 1L * 1024 * 1024;   // 1 MB
        public const long MaxTotalScanBytes      = 50L * 1024 * 1024;  // 50 MB cap across all results

        public List<WorkspaceFileInfo> Scan(IEnumerable<string> roots, IEnumerable<string> patterns = null)
        {
            var results = new List<WorkspaceFileInfo>();
            var effectivePatterns = (patterns?.ToList() ?? new List<string>()).Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
            if (effectivePatterns.Count == 0) effectivePatterns = DefaultPatterns.ToList();

            long totalBytes = 0;

            foreach (var root in (roots ?? Array.Empty<string>()).Where(r => !string.IsNullOrWhiteSpace(r)))
            {
                string normalisedRoot;
                try { normalisedRoot = Path.GetFullPath(root); }
                catch (Exception ex)
                {
                    ("WorkspaceScanner: bad root path '" + root + "': " + ex.Message).LogDebug("AiWorkspace");
                    continue;
                }

                if (!Directory.Exists(normalisedRoot))
                {
                    ("WorkspaceScanner: root does not exist: " + normalisedRoot).LogDebug("AiWorkspace");
                    continue;
                }

                var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
                foreach (var p in effectivePatterns)
                {
                    if (p.StartsWith("!")) matcher.AddExclude(p.Substring(1));
                    else matcher.AddInclude(p);
                }

                Microsoft.Extensions.FileSystemGlobbing.PatternMatchingResult matchResult;
                try
                {
                    matchResult = matcher.Execute(new Microsoft.Extensions.FileSystemGlobbing.Abstractions.DirectoryInfoWrapper(new DirectoryInfo(normalisedRoot)));
                }
                catch (Exception ex)
                {
                    ("WorkspaceScanner: scan failed for '" + normalisedRoot + "': " + ex.Message).LogDebug("AiWorkspace");
                    continue;
                }

                var perRoot = 0;
                foreach (var match in matchResult.Files)
                {
                    if (perRoot >= MaxFilesPerRoot) break;
                    if (totalBytes >= MaxTotalScanBytes) break;

                    string abs;
                    try { abs = Path.GetFullPath(Path.Combine(normalisedRoot, match.Path)); }
                    catch { continue; }

                    FileInfo fi;
                    try { fi = new FileInfo(abs); }
                    catch { continue; }
                    if (!fi.Exists) continue;
                    if (fi.Length > MaxIndividualFileBytes) continue;

                    results.Add(new WorkspaceFileInfo
                    {
                        Root         = normalisedRoot,
                        RelativePath = match.Path.Replace('\\', '/'),
                        AbsolutePath = abs,
                        SizeBytes    = fi.Length,
                        ModifiedUtc  = fi.LastWriteTimeUtc
                    });
                    perRoot++;
                    totalBytes += fi.Length;
                }
            }

            return results
                .OrderBy(r => r.Root, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>Read the contents of operator-selected files into a single
        /// preamble string, capped at <paramref name="maxBytes"/>. Files past the
        /// cap are listed by name but their bodies skipped. Each file is wrapped
        /// in a clear delimiter so the model can tell where one ends and another
        /// begins.</summary>
        public string BuildKnowledgePreamble(IEnumerable<string> selectedAbsolutePaths, int maxBytes = 64 * 1024)
        {
            if (selectedAbsolutePaths == null) return string.Empty;
            var sb = new System.Text.StringBuilder();
            long remaining = maxBytes;
            var truncated = new List<string>();

            foreach (var path in selectedAbsolutePaths.Where(p => !string.IsNullOrWhiteSpace(p)))
            {
                string content;
                try
                {
                    var fi = new FileInfo(path);
                    if (!fi.Exists) continue;
                    if (fi.Length > remaining) { truncated.Add(path); continue; }
                    content = File.ReadAllText(path);
                }
                catch (Exception ex)
                {
                    ("WorkspaceScanner.BuildKnowledgePreamble: read failed for '" + path + "': " + ex.Message).LogDebug("AiWorkspace");
                    continue;
                }

                var header = "\n\n--- [" + path + "] ---\n";
                var block = header + content;
                if (block.Length > remaining) { truncated.Add(path); continue; }

                sb.Append(block);
                remaining -= block.Length;
            }

            if (truncated.Count > 0)
            {
                sb.AppendLine().AppendLine("(skipped due to byte budget: " + string.Join(", ", truncated) + ")");
            }
            return sb.ToString();
        }
    }
}
