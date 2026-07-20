using System.Collections.Generic;
using Rxns.DDD.BoundedContext;

namespace Rxns.AppStatus.Host.Monitor.Events
{
    // ---------------------------------------------------------------------------
    // Domain events for the MonitorRoot aggregate. One file because they're all
    // small POCOs sharing the same conceptual surface; splitting them per file
    // would dilute discoverability.
    // ---------------------------------------------------------------------------

    /// <summary>Operator switched between Manual / Semi / Auto modes.</summary>
    public class MonitorModeChanged : DomainEvent
    {
        public string Mode { get; set; }  // "manual" | "semi" | "auto"
    }

    /// <summary>Operator toggled an IMonitorSource on or off.</summary>
    public class MonitorSourceToggled : DomainEvent
    {
        public string SourceId { get; set; }
        public bool Enabled { get; set; }
    }

    /// <summary>AI engine returned a new suggestion (passed dedupe).</summary>
    public class MonitorSuggestionRaised : DomainEvent
    {
        public string SuggestionId { get; set; }
        public string EngineId { get; set; }
        public string Category { get; set; }
        public string Severity { get; set; }
        public string Title { get; set; }
        public string Rationale { get; set; }
        public string DedupeKey { get; set; }
        public IList<MonitorEvent> Evidence { get; set; }
        public IList<MonitorSuggestedAction> SuggestedActions { get; set; }
    }

    /// <summary>Operator acknowledged a suggestion — removes from the active list.</summary>
    public class MonitorSuggestionAcked : DomainEvent
    {
        public string SuggestionId { get; set; }
    }

    /// <summary>Operator snoozed a suggestion for a duration; suppress its
    /// dedupe key from re-surfacing during that window.</summary>
    public class MonitorSuggestionSnoozed : DomainEvent
    {
        public string SuggestionId { get; set; }
        public int SnoozeMinutes { get; set; }
    }

    /// <summary>Operator added (Tool, ArgSchemaHash) to the trust list. In Auto
    /// mode, matching actions run without prompting.</summary>
    public class TrustedActionAdded : DomainEvent
    {
        public string Tool { get; set; }
        public string ArgSchemaHash { get; set; }
        public string Label { get; set; }   // optional human-readable hint
    }

    /// <summary>Operator revoked a previously-trusted action.</summary>
    public class TrustedActionRevoked : DomainEvent
    {
        public string Tool { get; set; }
        public string ArgSchemaHash { get; set; }
    }
}
