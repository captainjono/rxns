using System;
using System.Collections.Generic;
using System.Linq;
using Rxns.AppStatus.Host.Monitor.Events;
using Rxns.DDD.BoundedContext;

namespace Rxns.AppStatus.Host.Monitor
{
    /// <summary>
    /// Possible monitor-mode policies. Drives the heal/fix gating model.
    /// </summary>
    public enum MonitorMode
    {
        /// <summary>Every suggested action prompts the operator before running.
        /// Trust list is consulted but only suggests "you've used this before"
        /// — never auto-runs.</summary>
        Manual = 0,

        /// <summary>Actions run after a single confirmation; on completion the
        /// UI offers "trust this command" to persist it. Subsequent matches
        /// run more smoothly (next mode up).</summary>
        Semi = 1,

        /// <summary>Trusted (Tool, ArgSchemaHash) actions run silently when
        /// suggested. Anything outside the trust list still prompts. Use
        /// carefully — pairs with a tight trust list.</summary>
        Auto = 2
    }

    /// <summary>
    /// Single-aggregate-per-host root. Holds operator-controlled state:
    /// mode, enabled sources, the trust list, and the active suggestion
    /// queue. Persisted as a stream of <see cref="DomainEvent"/>s under
    /// <c>.rxns/MonitorRoot/&lt;tenant&gt;_default</c> (via the standard
    /// <c>TapeArrayTenantModelRepository</c>; backed by the in-memory
    /// tape source in V1 — switch to a disk-backed tape source factory
    /// to persist across restarts).
    /// </summary>
    public class MonitorRoot : AggRoot
    {
        public MonitorMode Mode { get; private set; } = MonitorMode.Manual;
        public HashSet<string> EnabledSourceIds { get; private set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public List<TrustedAction> TrustedActions { get; private set; } = new List<TrustedAction>();
        public List<MonitorSuggestion> ActiveSuggestions { get; private set; } = new List<MonitorSuggestion>();

        /// <summary>Snooze suppress map: dedupeKey → "don't re-raise until" timestamp.</summary>
        public Dictionary<string, DateTime> SnoozedUntil { get; private set; } = new Dictionary<string, DateTime>();

        public MonitorRoot() { }

        public MonitorRoot(string tenant)
        {
            Tenant = tenant;
            EId = "default";  // single root per tenant — keeps the key simple
        }

        // ── command surface ───────────────────────────────────────────────────

        public void SwitchMode(MonitorMode mode)
        {
            if (mode == Mode) return;
            LogChange(new MonitorModeChanged { Mode = mode.ToString().ToLowerInvariant() });
        }

        public void ToggleSource(string sourceId, bool enabled)
        {
            if (string.IsNullOrWhiteSpace(sourceId)) return;
            var isOn = EnabledSourceIds.Contains(sourceId);
            if (isOn == enabled) return;
            LogChange(new MonitorSourceToggled { SourceId = sourceId, Enabled = enabled });
        }

        public void RaiseSuggestion(MonitorSuggestion suggestion)
        {
            if (suggestion == null) return;
            LogChange(new MonitorSuggestionRaised
            {
                SuggestionId = suggestion.Id,
                EngineId = suggestion.EngineId,
                Category = suggestion.Category,
                Severity = suggestion.Severity,
                Title = suggestion.Title,
                Rationale = suggestion.Rationale,
                DedupeKey = suggestion.DedupeKey,
                Evidence = suggestion.Evidence,
                SuggestedActions = suggestion.SuggestedActions
            });
        }

        public void AckSuggestion(string suggestionId)
        {
            if (!ActiveSuggestions.Any(s => s.Id == suggestionId)) return;
            LogChange(new MonitorSuggestionAcked { SuggestionId = suggestionId });
        }

        public void SnoozeSuggestion(string suggestionId, int snoozeMinutes)
        {
            if (!ActiveSuggestions.Any(s => s.Id == suggestionId)) return;
            if (snoozeMinutes <= 0) snoozeMinutes = 30;
            LogChange(new MonitorSuggestionSnoozed { SuggestionId = suggestionId, SnoozeMinutes = snoozeMinutes });
        }

        public void TrustAction(string tool, string argSchemaHash, string label = null)
        {
            if (string.IsNullOrWhiteSpace(tool) || string.IsNullOrWhiteSpace(argSchemaHash)) return;
            if (TrustedActions.Any(t => t.Tool == tool && t.ArgSchemaHash == argSchemaHash)) return;
            LogChange(new TrustedActionAdded { Tool = tool, ArgSchemaHash = argSchemaHash, Label = label });
        }

        public void RevokeTrust(string tool, string argSchemaHash)
        {
            if (!TrustedActions.Any(t => t.Tool == tool && t.ArgSchemaHash == argSchemaHash)) return;
            LogChange(new TrustedActionRevoked { Tool = tool, ArgSchemaHash = argSchemaHash });
        }

        public bool IsSnoozed(string dedupeKey)
        {
            if (string.IsNullOrEmpty(dedupeKey)) return false;
            if (!SnoozedUntil.TryGetValue(dedupeKey, out var until)) return false;
            if (DateTime.UtcNow >= until) { SnoozedUntil.Remove(dedupeKey); return false; }
            return true;
        }

        public bool IsTrusted(string tool, string argSchemaHash) =>
            TrustedActions.Any(t => t.Tool == tool && t.ArgSchemaHash == argSchemaHash);

        // ── apply handlers ────────────────────────────────────────────────────

        public void ApplyChange(MonitorModeChanged @event)
        {
            if (Enum.TryParse<MonitorMode>(@event.Mode, ignoreCase: true, out var m)) Mode = m;
        }

        public void ApplyChange(MonitorSourceToggled @event)
        {
            if (@event.Enabled) EnabledSourceIds.Add(@event.SourceId);
            else EnabledSourceIds.Remove(@event.SourceId);
        }

        public void ApplyChange(MonitorSuggestionRaised @event)
        {
            ActiveSuggestions.Add(new MonitorSuggestion
            {
                Id = @event.SuggestionId,
                RaisedAt = @event.Timestamp,
                EngineId = @event.EngineId,
                Category = @event.Category,
                Severity = @event.Severity,
                Title = @event.Title,
                Rationale = @event.Rationale,
                DedupeKey = @event.DedupeKey,
                Evidence = @event.Evidence ?? new List<MonitorEvent>(),
                SuggestedActions = @event.SuggestedActions ?? new List<MonitorSuggestedAction>(),
                Status = "open"
            });

            // Cap to a sane ceiling so very chatty hosts don't unbounded-grow
            // the in-memory list (and the future disk tape).
            const int Max = 500;
            if (ActiveSuggestions.Count > Max)
                ActiveSuggestions.RemoveRange(0, ActiveSuggestions.Count - Max);
        }

        public void ApplyChange(MonitorSuggestionAcked @event)
        {
            var s = ActiveSuggestions.FirstOrDefault(x => x.Id == @event.SuggestionId);
            if (s != null) ActiveSuggestions.Remove(s);
        }

        public void ApplyChange(MonitorSuggestionSnoozed @event)
        {
            var s = ActiveSuggestions.FirstOrDefault(x => x.Id == @event.SuggestionId);
            if (s == null) return;
            ActiveSuggestions.Remove(s);
            if (!string.IsNullOrEmpty(s.DedupeKey))
            {
                SnoozedUntil[s.DedupeKey] = DateTime.UtcNow.AddMinutes(@event.SnoozeMinutes);
            }
        }

        public void ApplyChange(TrustedActionAdded @event)
        {
            TrustedActions.Add(new TrustedAction
            {
                Tool = @event.Tool,
                ArgSchemaHash = @event.ArgSchemaHash,
                Label = @event.Label,
                AddedAt = @event.Timestamp
            });
        }

        public void ApplyChange(TrustedActionRevoked @event)
        {
            TrustedActions.RemoveAll(t => t.Tool == @event.Tool && t.ArgSchemaHash == @event.ArgSchemaHash);
        }
    }

    public class TrustedAction
    {
        public string Tool { get; set; }
        public string ArgSchemaHash { get; set; }
        public string Label { get; set; }
        public DateTime AddedAt { get; set; }
    }
}
