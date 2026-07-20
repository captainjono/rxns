using System;
using System.Collections.Generic;

namespace Rxns.AppStatus.Host.Monitor
{
    /// <summary>
    /// One AI-generated recommendation surfaced in the monitor pane. Engines
    /// return these as a JSON array; the parser fills <see cref="Id"/> and
    /// <see cref="RaisedAt"/> server-side.
    /// </summary>
    public class MonitorSuggestion
    {
        public string Id { get; set; }
        public DateTime RaisedAt { get; set; }

        /// <summary>Engine that generated the suggestion (e.g. "ollama-default",
        /// "claude-default"). Helps the user trust/distrust based on which
        /// engine is talking.</summary>
        public string EngineId { get; set; }

        /// <summary>"error" | "perf" | "slow-path" | "config" | "other".
        /// Free-form — the prompt steers the model toward these, but new
        /// categories surface fine.</summary>
        public string Category { get; set; }

        /// <summary>"info" | "warn" | "error".</summary>
        public string Severity { get; set; }

        public string Title { get; set; }
        public string Rationale { get; set; }

        /// <summary>Source events the suggestion cited as evidence. Surfaced in
        /// the UI so the user can drill into what the model actually saw.</summary>
        public IList<MonitorEvent> Evidence { get; set; } = new List<MonitorEvent>();

        /// <summary>Concrete actions the model proposes. Each becomes a button
        /// in the suggestion card — clicking runs through the heal/fix mode
        /// (Manual confirms, Semi runs + offers trust, Auto runs trusted ones).</summary>
        public IList<MonitorSuggestedAction> SuggestedActions { get; set; } = new List<MonitorSuggestedAction>();

        /// <summary>Dedupe key — <c>hash(category, title)</c>. Same key
        /// in a short window suppresses re-surfacing the same finding.</summary>
        public string DedupeKey { get; set; }

        /// <summary>"open" | "acked" | "snoozed". Owned by the aggregate.</summary>
        public string Status { get; set; } = "open";
    }

    /// <summary>
    /// A specific action the model is recommending. The user clicks to apply;
    /// the host runs it through whatever <see cref="MonitorMode"/> is active
    /// (Manual confirms, Semi runs + offers trust persistence, Auto runs
    /// silently when (Tool, ArgHash) is in the trust list).
    /// </summary>
    public class MonitorSuggestedAction
    {
        /// <summary>Tool handler name (matches an <see cref="Rxns.Ai.IAiToolHandler.Definition"/>'s
        /// Name). Empty when the action is informational only ("read this log line",
        /// "review the perf KQL").</summary>
        public string Tool { get; set; }

        /// <summary>JSON args for the tool call.</summary>
        public string ArgumentsJson { get; set; }

        /// <summary>Human label for the button.</summary>
        public string Label { get; set; }

        /// <summary>Hash of <c>(Tool, normalised(ArgumentsJson))</c>. Used as
        /// the trust-list key — same tool + same args can be auto-applied
        /// without re-prompting once trusted.</summary>
        public string ArgSchemaHash { get; set; }
    }
}
