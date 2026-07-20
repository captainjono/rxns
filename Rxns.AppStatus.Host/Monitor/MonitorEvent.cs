using System;

namespace Rxns.AppStatus.Host.Monitor
{
    /// <summary>
    /// One observed signal from a single <see cref="IMonitorSource"/>.
    /// Sources buffer these into <see cref="MonitorService"/>'s rolling window;
    /// the service batches them up and asks the configured AI engine for
    /// structured suggestions.
    ///
    /// <para>Sources stay generic by emitting these — they never call the AI
    /// directly. That keeps source impls simple, deterministic, and (for tests)
    /// trivial to fake.</para>
    /// </summary>
    public class MonitorEvent
    {
        public string SourceId { get; set; }       // which IMonitorSource emitted this
        public DateTime At { get; set; }           // observation time
        public string Category { get; set; }       // "log-error" | "probe-state-change" | "perf-spike" | ...
        public string Severity { get; set; }       // "info" | "warn" | "error"
        public string Title { get; set; }          // short headline, e.g. "OutOfMemoryException in myapp-prod"
        public string EvidenceJson { get; set; }   // structured payload the AI can read (small — keep under 2KB)

        /// <summary>Free-form correlation id from the source — e.g. ErrorId for
        /// AppStatus errors, probe name for probe sources. Used by dedupe and
        /// "open in chat" so the chat lands with the same evidence the suggestion
        /// drew from.</summary>
        public string CorrelationKey { get; set; }
    }
}
