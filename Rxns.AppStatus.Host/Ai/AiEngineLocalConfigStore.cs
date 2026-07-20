using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rxns.AppStatus.Host.AppInsights;
using Rxns.Logging;

namespace Rxns.AppStatus.Host.Ai
{
    /// <summary>
    /// Read/write surface for <c>appstatus.local.config</c> — a sibling to the
    /// pristine <c>appstatus.config</c> that holds runtime-added engines. Layered
    /// on top of the base file by <see cref="AiModule.LoadOptions"/> at startup,
    /// so operator additions survive restart.
    ///
    /// <para>Only touches the <c>Ai.Engines</c> array; never rewrites the base
    /// <c>appstatus.config</c>. If <c>appstatus.local.config</c> doesn't exist
    /// yet, the first write creates it.</para>
    /// </summary>
    /// <summary>POCO for the workspace section of <c>appstatus.local.config</c>.
    /// Held separately from the engine list so the two sets of operator
    /// choices don't collide on writes.</summary>
    public class WorkspaceConfig
    {
        public List<string> Roots                  { get; set; } = new List<string>();
        public List<string> DiscoveryPatterns      { get; set; } = new List<string>();
        public List<string> SelectedKnowledgeFiles { get; set; } = new List<string>();
    }

    public class AiEngineLocalConfigStore
    {
        private const string FileName = "appstatus.local.config";
        private static readonly object _lock = new object();

        /// <summary>Load the engine list from <c>appstatus.local.config</c>. Returns
        /// an empty list when the file is missing or malformed.</summary>
        public List<AiEngineCfg> Load()
        {
            var path = ResolvePath(forWrite: false);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return new List<AiEngineCfg>();

            try
            {
                var json = File.ReadAllText(path);
                var doc = JObject.Parse(json);
                var engines = doc["Ai"]?["Engines"] as JArray;
                if (engines == null) return new List<AiEngineCfg>();
                return engines.OfType<JObject>()
                    .Select(e => e.ToObject<AiEngineCfg>())
                    .Where(e => e != null && !string.IsNullOrWhiteSpace(e.Id) && !string.IsNullOrWhiteSpace(e.Kind))
                    .ToList();
            }
            catch (Exception ex)
            {
                ("AiEngineLocalConfigStore.Load failed for '" + path + "': " + ex.Message).LogDebug("Ai");
                return new List<AiEngineCfg>();
            }
        }

        /// <summary>Read the persisted <c>Ai.ProjectContext</c> string. Returns
        /// null when the file or the field is missing.</summary>
        public string LoadProjectContext()
        {
            var path = ResolvePath(forWrite: false);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try
            {
                var doc = JObject.Parse(File.ReadAllText(path));
                return (string)doc["Ai"]?["ProjectContext"];
            }
            catch { return null; }
        }

        /// <summary>Load persisted embeddings engine declarations. Same shape
        /// as <see cref="Load"/> (chat engines) but reads <c>Ai.EmbeddingsEngines</c>.</summary>
        public List<AiEngineCfg> LoadEmbeddingsEngines()
        {
            var path = ResolvePath(forWrite: false);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return new List<AiEngineCfg>();
            try
            {
                var doc = JObject.Parse(File.ReadAllText(path));
                var arr = doc["Ai"]?["EmbeddingsEngines"] as JArray;
                if (arr == null) return new List<AiEngineCfg>();
                return arr.OfType<JObject>().Select(e => e.ToObject<AiEngineCfg>())
                    .Where(e => e != null && !string.IsNullOrWhiteSpace(e.Id) && !string.IsNullOrWhiteSpace(e.Kind))
                    .ToList();
            }
            catch { return new List<AiEngineCfg>(); }
        }

        /// <summary>Add/replace an embeddings engine by id.</summary>
        public void UpsertEmbeddingsEngine(AiEngineCfg cfg)
        {
            if (cfg == null || string.IsNullOrWhiteSpace(cfg.Id)) return;
            MutateEmbeddings(list =>
            {
                list.RemoveAll(e => string.Equals(e.Id, cfg.Id, StringComparison.OrdinalIgnoreCase));
                list.Add(cfg);
            });
        }

        public void RemoveEmbeddingsEngine(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            MutateEmbeddings(list => list.RemoveAll(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase)));
        }

        private void MutateEmbeddings(Action<List<AiEngineCfg>> apply)
        {
            lock (_lock)
            {
                var path = ResolvePath(forWrite: true);
                if (path == null) return;
                var list = LoadEmbeddingsEngines();
                apply(list);

                JObject doc;
                if (File.Exists(path))
                {
                    try { doc = JObject.Parse(File.ReadAllText(path)); }
                    catch { doc = new JObject(); }
                }
                else doc = new JObject();

                var ai = doc["Ai"] as JObject ?? new JObject();
                ai["EmbeddingsEngines"] = JArray.FromObject(list);
                doc["Ai"] = ai;
                try { File.WriteAllText(path, doc.ToString(Formatting.Indented)); }
                catch (Exception ex) { ("AiEngineLocalConfigStore.MutateEmbeddings failed for '" + path + "': " + ex.Message).LogDebug("Ai"); }
            }
        }

        /// <summary>Load the persisted workspace selection (roots + selected
        /// knowledge files + discovery patterns) — anything missing comes back
        /// as an empty list.</summary>
        public WorkspaceConfig LoadWorkspace()
        {
            var cfg = new WorkspaceConfig();
            var path = ResolvePath(forWrite: false);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return cfg;
            try
            {
                var doc = JObject.Parse(File.ReadAllText(path));
                var ws = doc["Ai"]?["Workspace"] as JObject;
                if (ws == null) return cfg;
                cfg.Roots                  = (ws["Roots"]              as JArray)?.Select(t => (string)t).Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? new List<string>();
                cfg.DiscoveryPatterns      = (ws["DiscoveryPatterns"]  as JArray)?.Select(t => (string)t).Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? new List<string>();
                cfg.SelectedKnowledgeFiles = (ws["SelectedKnowledgeFiles"] as JArray)?.Select(t => (string)t).Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? new List<string>();
            }
            catch { /* malformed local cfg — caller handles defaults */ }
            return cfg;
        }

        /// <summary>Persist the workspace selection. Pass null entries to leave
        /// the existing value untouched; pass an empty list to clear it.</summary>
        public void SaveWorkspace(List<string> roots, List<string> patterns, List<string> selectedFiles)
        {
            lock (_lock)
            {
                var path = ResolvePath(forWrite: true);
                if (path == null) return;

                JObject doc;
                if (File.Exists(path))
                {
                    try { doc = JObject.Parse(File.ReadAllText(path)); }
                    catch { doc = new JObject(); }
                }
                else doc = new JObject();

                var ai = doc["Ai"] as JObject ?? new JObject();
                var ws = ai["Workspace"] as JObject ?? new JObject();
                if (roots         != null) ws["Roots"]                  = JArray.FromObject(roots);
                if (patterns      != null) ws["DiscoveryPatterns"]      = JArray.FromObject(patterns);
                if (selectedFiles != null) ws["SelectedKnowledgeFiles"] = JArray.FromObject(selectedFiles);
                ai["Workspace"] = ws;
                doc["Ai"] = ai;

                try { File.WriteAllText(path, doc.ToString(Formatting.Indented)); }
                catch (Exception ex) { ("AiEngineLocalConfigStore.SaveWorkspace failed for '" + path + "': " + ex.Message).LogDebug("Ai"); }
            }
        }

        /// <summary>Write or clear the <c>Ai.ProjectContext</c> string while
        /// preserving every other field (Engines, etc.).</summary>
        public void SaveProjectContext(string text)
        {
            lock (_lock)
            {
                var path = ResolvePath(forWrite: true);
                if (path == null) return;

                JObject doc;
                if (File.Exists(path))
                {
                    try { doc = JObject.Parse(File.ReadAllText(path)); }
                    catch { doc = new JObject(); }
                }
                else
                {
                    doc = new JObject();
                }

                var ai = doc["Ai"] as JObject ?? new JObject();
                if (string.IsNullOrEmpty(text)) ai.Remove("ProjectContext");
                else                            ai["ProjectContext"] = text;
                doc["Ai"] = ai;

                try { File.WriteAllText(path, doc.ToString(Formatting.Indented)); }
                catch (Exception ex) { ("AiEngineLocalConfigStore.SaveProjectContext failed for '" + path + "': " + ex.Message).LogDebug("Ai"); }
            }
        }

        /// <summary>Add or replace an engine entry by Id. Other entries are
        /// preserved verbatim.</summary>
        public void Upsert(AiEngineCfg cfg)
        {
            if (cfg == null || string.IsNullOrWhiteSpace(cfg.Id)) return;
            Mutate(list =>
            {
                list.RemoveAll(e => string.Equals(e.Id, cfg.Id, StringComparison.OrdinalIgnoreCase));
                list.Add(cfg);
            });
        }

        /// <summary>Remove an engine entry by Id. No-op if not present.</summary>
        public void Remove(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            Mutate(list => list.RemoveAll(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase)));
        }

        private void Mutate(Action<List<AiEngineCfg>> apply)
        {
            lock (_lock)
            {
                var path = ResolvePath(forWrite: true);
                if (path == null)
                {
                    ("AiEngineLocalConfigStore: no writable directory resolved — runtime engine changes will not persist across restart.").LogDebug("Ai");
                    return;
                }

                var list = Load();
                apply(list);

                JObject doc;
                if (File.Exists(path))
                {
                    try { doc = JObject.Parse(File.ReadAllText(path)); }
                    catch { doc = new JObject(); }
                }
                else
                {
                    doc = new JObject();
                }

                var ai = doc["Ai"] as JObject ?? new JObject();
                ai["Engines"] = JArray.FromObject(list);
                doc["Ai"] = ai;

                try
                {
                    File.WriteAllText(path, doc.ToString(Formatting.Indented));
                }
                catch (Exception ex)
                {
                    ("AiEngineLocalConfigStore.Save failed for '" + path + "': " + ex.Message).LogDebug("Ai");
                }
            }
        }

        /// <summary>
        /// Resolve the path to <c>appstatus.local.config</c>. Reads and writes
        /// MUST agree on the location — earlier this method had asymmetric
        /// fallbacks (write fell back to <see cref="AppContext.BaseDirectory"/>
        /// when no base <c>appstatus.config</c> existed; read returned null),
        /// so operator-added engines wrote to disk but never loaded back on
        /// restart. Now both sides walk the same chain and end with the
        /// <c>BaseDirectory/appstatus.local.config</c> fallback. There's also
        /// a second-pass scan to pick up an existing <c>appstatus.local.config</c>
        /// even when no base config is present.
        /// </summary>
        private static string ResolvePath(bool forWrite)
        {
            var dirFromEnv = Environment.GetEnvironmentVariable(AppInsightsRxnCfg.Loader.ConfigDirEnv);
            if (!string.IsNullOrWhiteSpace(dirFromEnv) && Directory.Exists(dirFromEnv))
                return Path.Combine(dirFromEnv, FileName);

            var probes = new[]
            {
                AppContext.BaseDirectory,
                Path.Combine(AppContext.BaseDirectory, "config"),
                Path.Combine(AppContext.BaseDirectory, "..")
            };

            // 1. Prefer the directory that holds the base appstatus.config so the
            //    two files live side-by-side when both exist.
            foreach (var p in probes)
            {
                try
                {
                    if (File.Exists(Path.Combine(p, "appstatus.config")))
                        return Path.GetFullPath(Path.Combine(p, FileName));
                }
                catch { /* ignore — try the next probe */ }
            }

            // 2. No base config? Re-walk the probes looking for an existing
            //    appstatus.local.config so loads still find it after a fresh write.
            foreach (var p in probes)
            {
                try
                {
                    var candidate = Path.Combine(p, FileName);
                    if (File.Exists(candidate)) return Path.GetFullPath(candidate);
                }
                catch { /* ignore */ }
            }

            // 3. First-time write (or read with no file yet): both sides land
            //    on BaseDirectory so the next read finds what the write just put.
            return Path.Combine(AppContext.BaseDirectory, FileName);
        }
    }
}
