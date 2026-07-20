using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Rxns.AppStatus.Host.Ai.Workspace
{
    /// <summary>
    /// Validates filesystem paths against the configured workspace roots.
    /// EVERY tool that touches disk must call <see cref="ResolveOrThrow"/>
    /// before reading — otherwise the model can ask for arbitrary system
    /// files (e.g. <c>C:\Windows\System32\config\SAM</c>) via a sandbox
    /// escape. Centralising here means new tools can't forget the check.
    /// </summary>
    public static class WorkspacePathGuard
    {
        /// <summary>Resolve a model-supplied path to an absolute path AND verify
        /// it lives under one of the configured roots. Throws
        /// <see cref="UnauthorizedAccessException"/> if the resolved path
        /// escapes — handlers surface that as a tool error.</summary>
        public static string ResolveOrThrow(string path, IEnumerable<string> roots)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("path is required");

            string full;
            try { full = Path.GetFullPath(path); }
            catch (Exception ex)
            {
                throw new ArgumentException("invalid path '" + path + "': " + ex.Message);
            }

            var normalisedRoots = (roots ?? Array.Empty<string>())
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Select(r => { try { return Path.GetFullPath(r); } catch { return null; } })
                .Where(r => r != null)
                .ToList();

            if (normalisedRoots.Count == 0)
                throw new InvalidOperationException("no workspace roots configured; add one in Settings before using workspace tools");

            // OrdinalIgnoreCase because Windows is case-insensitive and the
            // model is likely to use whichever casing it has. Trailing
            // separator on the root forces a path-segment boundary check —
            // prevents "C:/jan/rxnsXX" from matching root "C:/jan/rxns".
            foreach (var root in normalisedRoots)
            {
                var rootWithSep = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (string.Equals(full, root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
                    || full.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase))
                {
                    return full;
                }
            }

            throw new UnauthorizedAccessException(
                "path '" + full + "' is outside the configured workspace roots: " + string.Join(",", normalisedRoots));
        }

        /// <summary>Same as <see cref="ResolveOrThrow"/> but returns null
        /// instead of throwing — used by handlers that want to surface a
        /// graceful tool-result-error rather than an exception.</summary>
        public static string TryResolve(string path, IEnumerable<string> roots, out string error)
        {
            try { error = null; return ResolveOrThrow(path, roots); }
            catch (Exception ex) { error = ex.Message; return null; }
        }
    }
}
