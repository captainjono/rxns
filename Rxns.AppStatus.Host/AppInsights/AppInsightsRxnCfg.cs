using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Rxns.AppInsights;
using Rxns.AppStatus.Host.Ai;

namespace Rxns.AppStatus.Host.AppInsights
{
    /// <summary>
    /// JSON file shape for AppInsights config loaded alongside the host binary.
    /// Follows a per-environment config file pattern (a
    /// <c>&lt;name&gt;.&lt;env&gt;.config</c> convention): a base file plus an
    /// optional env overlay, both discovered in
    /// <see cref="Loader.ConfigDirEnv"/> (default: probe well-known locations).
    ///
    /// Example <c>appstatus.config</c>:
    /// <code>
    /// {
    ///   "Targets": [
    ///     { "Name": "myapp-dev",      "SubscriptionId": "00000000-…", "ResourceGroup": "myapp-dev",      "AppName": "myapp-dev",      "DefaultEnabled": true  },
    ///     { "Name": "myapp-prod",     "SubscriptionId": "00000000-…", "ResourceGroup": "myapp-prod",     "AppName": "myapp-prod",     "DefaultEnabled": false },
    ///     { "Name": "myapp-staging",  "SubscriptionId": "00000000-…", "ResourceGroup": "myapp-staging",  "AppName": "myapp-staging",  "DefaultEnabled": false }
    ///   ]
    /// }
    /// </code>
    ///
    /// The UI reads <c>Targets[]</c> from <c>/api/appinsights/info</c> and renders one checkbox
    /// per target. Operators flip individual targets on to flatten queries across envs.
    /// </summary>
    public class AppInsightsRxnCfg
    {
        public List<AppInsightsTarget> Targets { get; set; } = new List<AppInsightsTarget>();

        /// <summary>
        /// Optional Ai block in the same rxncfg file. Declares every AI engine
        /// (Claude / Ollama / Foundry / CLI) the portal can route requests to —
        /// each entry becomes one row in the engine picker. When absent, the
        /// <c>AiModule</c> falls back to env-var conveniences
        /// (<c>CLAUDE_API_KEY</c>, <c>OLLAMA_URL</c>, …).
        /// </summary>
        public AiCfgSection Ai { get; set; }

        public class AiCfgSection
        {
            public List<AiEngineCfg> Engines { get; set; }
            public string DefaultEngineId { get; set; }
            public bool? ReadOnly { get; set; }
            public string SystemPrompt { get; set; }
        }

        public static class Loader
        {
            public const string DefaultFileName = "appstatus.config";
            public const string ConfigDirEnv = "RXNS_APPSTATUS_CONFIG_DIR";
            public const string EnvOverlayEnv = "RXNS_ENV";

            /// <summary>
            /// Resolve cfg by:
            /// 1. <c>RXNS_APPSTATUS_CONFIG_DIR/appstatus.config</c>
            ///    (or the probed location next to AppContext.BaseDirectory)
            /// 2. Overlay with <c>appstatus.&lt;env&gt;.config</c> if <c>RXNS_ENV</c> is set
            /// 3. Fall back to empty Targets[] when nothing resolves — caller's UI shows
            ///    "no targets configured".
            /// </summary>
            public static AppInsightsBrowserOptions Resolve()
            {
                var opts = new AppInsightsBrowserOptions();
                var dir = ResolveConfigDir();
                if (string.IsNullOrWhiteSpace(dir)) return opts;

                var basePath = Path.Combine(dir, DefaultFileName);
                MergeFrom(basePath, opts);

                var env = Environment.GetEnvironmentVariable(EnvOverlayEnv);
                if (!string.IsNullOrWhiteSpace(env))
                {
                    var overlay = Path.Combine(dir, "appstatus." + env + ".config");
                    MergeFrom(overlay, opts);
                }

                return opts;
            }

            private static string ResolveConfigDir()
            {
                var fromEnv = Environment.GetEnvironmentVariable(ConfigDirEnv);
                if (!string.IsNullOrWhiteSpace(fromEnv) && Directory.Exists(fromEnv)) return fromEnv;

                var probes = new[]
                {
                    AppContext.BaseDirectory,
                    Path.Combine(AppContext.BaseDirectory, "config"),
                    Path.Combine(AppContext.BaseDirectory, ".."),
                };
                foreach (var p in probes)
                {
                    try
                    {
                        if (File.Exists(Path.Combine(p, DefaultFileName))) return Path.GetFullPath(p);
                    }
                    catch { /* ignore — try the next probe */ }
                }
                return null;
            }

            private static void MergeFrom(string path, AppInsightsBrowserOptions opts)
            {
                var cfg = LoadCfgIfPresent(path);
                if (cfg?.Targets == null) return;

                // Overlay semantics: targets with the same Name replace; new names append.
                foreach (var t in cfg.Targets)
                {
                    if (t == null || string.IsNullOrWhiteSpace(t.Name)) continue;
                    var existing = opts.Targets.FindIndex(x => string.Equals(x.Name, t.Name, StringComparison.OrdinalIgnoreCase));
                    if (existing >= 0) opts.Targets[existing] = t;
                    else opts.Targets.Add(t);
                }
            }

            /// <summary>
            /// Read the raw <see cref="AppInsightsRxnCfg"/> (including the optional
            /// <see cref="AiCfgSection"/>) from a base file + optional env overlay.
            /// Used by <c>AiModule</c> so engine config lives in the same JSON.
            /// Returns an empty cfg when nothing resolves — never throws.
            /// </summary>
            public static AppInsightsRxnCfg ResolveRaw()
            {
                var dir = ResolveConfigDir();
                var result = new AppInsightsRxnCfg();
                if (string.IsNullOrWhiteSpace(dir)) return result;

                Merge(result, LoadCfgIfPresent(Path.Combine(dir, DefaultFileName)));

                var env = Environment.GetEnvironmentVariable(EnvOverlayEnv);
                if (!string.IsNullOrWhiteSpace(env))
                {
                    var overlay = Path.Combine(dir, "appstatus." + env + ".config");
                    Merge(result, LoadCfgIfPresent(overlay));
                }
                return result;
            }

            private static AppInsightsRxnCfg LoadCfgIfPresent(string path)
            {
                if (!File.Exists(path)) return null;
                try
                {
                    var json = File.ReadAllText(path);
                    return JsonConvert.DeserializeObject<AppInsightsRxnCfg>(json);
                }
                catch
                {
                    // bad JSON → swallow; the host must stay up.
                    return null;
                }
            }

            private static void Merge(AppInsightsRxnCfg into, AppInsightsRxnCfg from)
            {
                if (from == null) return;
                if (from.Ai != null) into.Ai = from.Ai;   // overlay replaces wholesale
                if (from.Targets != null)
                {
                    foreach (var t in from.Targets)
                    {
                        if (t == null || string.IsNullOrWhiteSpace(t.Name)) continue;
                        var existing = into.Targets.FindIndex(x => string.Equals(x.Name, t.Name, StringComparison.OrdinalIgnoreCase));
                        if (existing >= 0) into.Targets[existing] = t;
                        else into.Targets.Add(t);
                    }
                }
            }
        }
    }
}
