using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rxns.Ai;
using Rxns.AppStatus.Host.Ai;
using Rxns.DDD.BoundedContext;
using Rxns.Logging;
using Rxns.WebApiNET5.NET5WebApiAdapters.RxnsApiAdapters;

namespace Rxns.AppStatus.Host.Monitor
{
    /// <summary>
    /// Orchestrator for monitor mode. Subscribes to enabled
    /// <see cref="IMonitorSource"/>s, batches their events into a rolling
    /// window, prompts the configured AI engine on a cadence, parses
    /// suggestions, dedupes against an LRU and the aggregate's snooze map,
    /// then writes <see cref="Events.MonitorSuggestionRaised"/> through the
    /// <see cref="ITenantModelRepository{T}"/>. Each raise is broadcast over
    /// SignalR as a <c>MonitorEvent</c> hub message so the portal pane lights
    /// up in real time.
    ///
    /// <para>One singleton per host. State (sources, mode, suggestions, trust
    /// list) is mirrored from the <see cref="MonitorRoot"/> on startup;
    /// command methods write the event AND update the mirror so reads stay
    /// cheap.</para>
    /// </summary>
    public class MonitorService : IDisposable
    {
        private const string Tenant = "default";

        private readonly AiChatEngineFactory _engines;
        private readonly AiOptions _aiOptions;
        private readonly IReadOnlyList<IMonitorSource> _sources;
        private readonly ITenantModelRepository<MonitorRoot> _repo;
        private readonly IHubContext<EventsHub> _hub;

        private MonitorRoot _state;
        private readonly object _stateLock = new object();

        // Per-source subscription so we can attach/detach as the operator
        // toggles checkboxes in the UI.
        private readonly ConcurrentDictionary<string, IDisposable> _sourceSubs = new ConcurrentDictionary<string, IDisposable>();

        // Rolling window of recent events fed to the AI prompt. Capacity-bounded;
        // newest entries drop old ones once the cap is hit.
        private const int RollingWindowCap = 200;
        private readonly Queue<MonitorEvent> _window = new Queue<MonitorEvent>();
        private readonly object _windowLock = new object();

        // Counts events accumulated since last AI call so we know when to flush
        // on the "20 errors batched" trigger.
        private int _unprocessedCount;
        private DateTime _lastFlush = DateTime.UtcNow;

        // Suggestion dedupe — short-window LRU keyed on (category|title) hash.
        // Sits alongside the aggregate's longer-lived Snoozed map: snooze is
        // operator-driven, this is automatic burst suppression.
        private readonly Dictionary<string, DateTime> _recentDedupe = new Dictionary<string, DateTime>();
        private static readonly TimeSpan DedupeWindow = TimeSpan.FromMinutes(15);

        private readonly IDisposable _flushTicker;

        public MonitorService(
            AiChatEngineFactory engines,
            AiOptions aiOptions,
            IEnumerable<IMonitorSource> sources,
            ITenantModelRepository<MonitorRoot> repo,
            IHubContext<EventsHub> hub)
        {
            _engines = engines;
            _aiOptions = aiOptions ?? new AiOptions();
            _sources = (sources ?? new IMonitorSource[0]).Where(s => s != null).ToList();
            _repo = repo;
            _hub = hub;

            // Hydrate the aggregate from the repo (in-memory tape in v1; survives
            // for the lifetime of the host process). Any sources the operator
            // had enabled in a previous session re-subscribe on startup.
            _state = HydrateOrCreate();
            ReconcileSubscriptions();

            // Time-based flush: every 60s, if we've buffered events, prompt the
            // AI. Combined with the count-based trigger inside OnSourceEvent.
            _flushTicker = Observable.Interval(TimeSpan.FromSeconds(60), Scheduler.Default)
                .Subscribe(_ => TryFlushAsync(reason: "time").Forget());
        }

        // ── reads (UI consumes via REST) ──────────────────────────────────────

        public MonitorMode Mode => SnapshotState(s => s.Mode);
        public IReadOnlyList<MonitorSuggestion> ActiveSuggestions =>
            SnapshotState(s => s.ActiveSuggestions.ToList());
        public IReadOnlyList<TrustedAction> Trusted =>
            SnapshotState(s => s.TrustedActions.ToList());
        public IReadOnlyCollection<string> EnabledSourceIds =>
            SnapshotState(s => s.EnabledSourceIds.ToList());

        public IReadOnlyList<IMonitorSource> AllSources => _sources;

        // ── commands (UI invokes via REST) ────────────────────────────────────

        public void SwitchMode(MonitorMode mode)
        {
            ApplyAndSave(s => s.SwitchMode(mode));
        }

        public void ToggleSource(string sourceId, bool enabled)
        {
            ApplyAndSave(s => s.ToggleSource(sourceId, enabled));
            ReconcileSubscriptions();
        }

        public void AckSuggestion(string id) => ApplyAndSave(s => s.AckSuggestion(id));
        public void SnoozeSuggestion(string id, int minutes) => ApplyAndSave(s => s.SnoozeSuggestion(id, minutes));
        public void TrustAction(string tool, string argHash, string label) => ApplyAndSave(s => s.TrustAction(tool, argHash, label));
        public void RevokeTrust(string tool, string argHash) => ApplyAndSave(s => s.RevokeTrust(tool, argHash));

        /// <summary>Manual "analyse now" — flush whatever's in the window
        /// regardless of triggers. Returns the count of new suggestions raised.</summary>
        public Task<int> AnalyseNowAsync() => TryFlushAsync(reason: "manual");

        public static string ComputeArgHash(string tool, string argumentsJson)
        {
            var canonical = (tool ?? "").Trim() + "|" + Canonicalise(argumentsJson);
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(canonical));
            return BitConverter.ToString(bytes, 0, 8).Replace("-", "").ToLowerInvariant();
        }

        // ── source subscription lifecycle ─────────────────────────────────────

        private void ReconcileSubscriptions()
        {
            var wanted = SnapshotState(s => new HashSet<string>(s.EnabledSourceIds, StringComparer.OrdinalIgnoreCase));

            // detach removed
            foreach (var kv in _sourceSubs.ToArray())
            {
                if (!wanted.Contains(kv.Key))
                {
                    if (_sourceSubs.TryRemove(kv.Key, out var sub)) sub.Dispose();
                }
            }

            // attach newly-wanted, but only if available
            foreach (var src in _sources)
            {
                if (!wanted.Contains(src.Id)) continue;
                if (_sourceSubs.ContainsKey(src.Id)) continue;
                if (!src.IsAvailable) continue;

                var local = src;
                var sub = src.Events.Subscribe(
                    ev => OnSourceEvent(local, ev),
                    err => ("MonitorService source '" + local.Id + "' faulted: " + err.Message).LogDebug("MonitorService"));
                _sourceSubs[src.Id] = sub;
            }
        }

        private void OnSourceEvent(IMonitorSource src, MonitorEvent ev)
        {
            if (ev == null) return;
            ev.SourceId = src.Id;

            lock (_windowLock)
            {
                _window.Enqueue(ev);
                while (_window.Count > RollingWindowCap) _window.Dequeue();
                _unprocessedCount++;
            }

            // Count-based flush: 20 unprocessed events tips us into the AI call.
            if (Interlocked.CompareExchange(ref _unprocessedCount, 0, 0) >= 20)
            {
                TryFlushAsync(reason: "count").Forget();
            }
        }

        // ── AI prompt cycle ───────────────────────────────────────────────────

        private readonly SemaphoreSlim _flushGate = new SemaphoreSlim(1, 1);

        private async Task<int> TryFlushAsync(string reason)
        {
            if (!await _flushGate.WaitAsync(0).ConfigureAwait(false)) return 0;
            try
            {
                MonitorEvent[] snapshot;
                lock (_windowLock)
                {
                    if (_window.Count == 0) return 0;
                    snapshot = _window.ToArray();
                    _unprocessedCount = 0;
                    _lastFlush = DateTime.UtcNow;
                }

                IAiChatEngine engine;
                try { engine = _engines.Pick(null); }
                catch (Exception ex)
                {
                    ("MonitorService.flush skipped — " + ex.Message + " (reason=" + reason + ")").LogDebug("MonitorService");
                    return 0;
                }

                var systemPrompt = BuildSystemPrompt();
                var userPrompt = BuildUserPrompt(snapshot, reason);

                AiChatResponse response;
                try
                {
                    response = await engine.CompleteAsync(new AiChatRequest
                    {
                        Messages = new List<AiChatMessage> { new AiChatMessage { Role = "user", Content = userPrompt } },
                        SystemPrompt = systemPrompt,
                        AllowToolCalls = false   // monitor pass is analysis only — tool calls happen via heal/fix
                    }).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    ("MonitorService.flush AI call failed: " + ex.Message).LogDebug("MonitorService");
                    return 0;
                }

                var suggestions = ParseSuggestions(response, snapshot);
                if (suggestions.Count == 0) return 0;

                var raisedCount = 0;
                foreach (var s in suggestions)
                {
                    s.EngineId = engine.EngineId;
                    s.DedupeKey ??= ComputeDedupeKey(s.Category, s.Title);
                    if (ShouldSkipDuplicate(s.DedupeKey)) continue;
                    RememberDedupe(s.DedupeKey);

                    s.Id = Guid.NewGuid().ToString();
                    s.RaisedAt = DateTime.UtcNow;

                    ApplyAndSave(root => root.RaiseSuggestion(s));
                    raisedCount++;

                    // SignalR push so the monitor pane lights up immediately.
                    if (_hub != null)
                    {
                        try
                        {
                            await _hub.Clients.All.SendAsync("MonitorEvent", new
                            {
                                kind = "suggestionRaised",
                                suggestion = s
                            }).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            ("MonitorService SignalR push failed: " + ex.Message).LogDebug("MonitorService");
                        }
                    }
                }

                return raisedCount;
            }
            finally
            {
                _flushGate.Release();
            }
        }

        private string BuildSystemPrompt()
        {
            return
                "You are the suggestion engine behind the rxns support portal's monitor pane. " +
                "Given a list of recent observed signals (log errors, probe transitions, perf spikes), output a JSON object with a single field `suggestions`: an array of zero or more recommendations. " +
                "Each suggestion has fields: category ('error'|'perf'|'slow-path'|'config'|'other'), severity ('info'|'warn'|'error'), title (short headline, <80 chars), rationale (1–3 sentences citing the evidence), and suggestedActions (array; each entry has `label` and optionally `tool` + `argumentsJson` if a specific tool call would help). " +
                "Be conservative: prefer zero suggestions over noise. Group related signals into one suggestion. Reply with raw JSON only — no markdown fences, no preamble.";
        }

        private string BuildUserPrompt(MonitorEvent[] events, string reason)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Trigger: " + reason + " (" + events.Length + " signals in the last window)");
            sb.AppendLine("Signals (newest last):");
            foreach (var e in events)
            {
                sb.Append("- ").Append(e.At.ToUniversalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture))
                  .Append(" [").Append(e.SourceId).Append("/").Append(e.Severity).Append("] ")
                  .Append(e.Category).Append(": ").AppendLine(e.Title);
                if (!string.IsNullOrWhiteSpace(e.EvidenceJson))
                {
                    sb.Append("    evidence: ").AppendLine(Truncate(e.EvidenceJson, 600));
                }
            }
            return sb.ToString();
        }

        private List<MonitorSuggestion> ParseSuggestions(AiChatResponse response, MonitorEvent[] evidencePool)
        {
            var list = new List<MonitorSuggestion>();
            if (response == null || string.IsNullOrWhiteSpace(response.AssistantText)) return list;
            if (response.StopReason == "error") return list;

            string text = response.AssistantText.Trim();

            // Some local models leak markdown fences despite the instruction.
            // Strip a leading/trailing ``` block if present.
            if (text.StartsWith("```"))
            {
                var firstNl = text.IndexOf('\n');
                if (firstNl > 0) text = text.Substring(firstNl + 1);
                if (text.EndsWith("```")) text = text.Substring(0, text.Length - 3);
                text = text.Trim();
            }

            JObject doc;
            try { doc = JObject.Parse(text); }
            catch
            {
                ("MonitorService: model returned non-JSON; first 200 chars: " + Truncate(text, 200)).LogDebug("MonitorService");
                return list;
            }

            var arr = doc["suggestions"] as JArray;
            if (arr == null) return list;

            foreach (var item in arr.OfType<JObject>())
            {
                var s = new MonitorSuggestion
                {
                    Category = (string)item["category"] ?? "other",
                    Severity = (string)item["severity"] ?? "info",
                    Title = (string)item["title"],
                    Rationale = (string)item["rationale"],
                    Evidence = evidencePool.ToList(),   // attach the batch for full traceability
                    SuggestedActions = new List<MonitorSuggestedAction>()
                };

                var actions = item["suggestedActions"] as JArray;
                if (actions != null)
                {
                    foreach (var a in actions.OfType<JObject>())
                    {
                        var tool = (string)a["tool"];
                        var argsJson = a["argumentsJson"]?.ToString(Formatting.None);
                        s.SuggestedActions.Add(new MonitorSuggestedAction
                        {
                            Label = (string)a["label"],
                            Tool = tool,
                            ArgumentsJson = argsJson,
                            ArgSchemaHash = string.IsNullOrWhiteSpace(tool) ? null : ComputeArgHash(tool, argsJson)
                        });
                    }
                }

                if (string.IsNullOrWhiteSpace(s.Title)) continue;
                list.Add(s);
            }

            return list;
        }

        // ── helpers ───────────────────────────────────────────────────────────

        private MonitorRoot HydrateOrCreate()
        {
            if (_repo == null) return new MonitorRoot(Tenant);
            try
            {
                var loaded = _repo.GetById(Tenant, "default").Wait();
                if (loaded == null) loaded = new MonitorRoot(Tenant);
                return loaded;
            }
            catch
            {
                return new MonitorRoot(Tenant);
            }
        }

        private void ApplyAndSave(Action<MonitorRoot> mutate)
        {
            lock (_stateLock)
            {
                mutate(_state);
                if (_repo != null)
                {
                    try { _repo.Save(Tenant, _state); }
                    catch (Exception ex)
                    {
                        ("MonitorService repo.Save failed: " + ex.Message).LogDebug("MonitorService");
                    }
                }
            }
        }

        private T SnapshotState<T>(Func<MonitorRoot, T> read)
        {
            lock (_stateLock) return read(_state);
        }

        private bool ShouldSkipDuplicate(string dedupeKey)
        {
            if (string.IsNullOrEmpty(dedupeKey)) return false;
            lock (_recentDedupe)
            {
                // Aggregate-level snooze trumps short-window dedupe.
                lock (_stateLock) { if (_state.IsSnoozed(dedupeKey)) return true; }

                if (_recentDedupe.TryGetValue(dedupeKey, out var at) && DateTime.UtcNow - at < DedupeWindow)
                    return true;
                return false;
            }
        }

        private void RememberDedupe(string dedupeKey)
        {
            if (string.IsNullOrEmpty(dedupeKey)) return;
            lock (_recentDedupe)
            {
                _recentDedupe[dedupeKey] = DateTime.UtcNow;
                if (_recentDedupe.Count > 500)
                {
                    var expired = _recentDedupe.Where(kv => DateTime.UtcNow - kv.Value > DedupeWindow).Select(kv => kv.Key).ToList();
                    foreach (var k in expired) _recentDedupe.Remove(k);
                }
            }
        }

        private static string ComputeDedupeKey(string category, string title)
        {
            return ((category ?? "").ToLowerInvariant() + "|" + (title ?? "").ToLowerInvariant()).Trim();
        }

        private static string Canonicalise(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return "";
            try
            {
                var tok = JToken.Parse(json);
                return tok.ToString(Formatting.None);
            }
            catch { return json.Trim(); }
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }

        public void Dispose()
        {
            _flushTicker?.Dispose();
            foreach (var kv in _sourceSubs) kv.Value.Dispose();
            _sourceSubs.Clear();
            _flushGate.Dispose();
        }
    }

    internal static class TaskExtensions
    {
        public static void Forget(this Task t) { _ = t.ContinueWith(_ => { }, TaskScheduler.Default); }
    }
}
