using System;
using System.Threading;
using Rxns.Interfaces;

namespace Rxns.Logging
{
    /// <summary>
    /// Possible log levels for the system
    /// </summary>
    public enum LogLevel
    {
        None = 0,
        Log,
        Info,
        Warning,
        Error,
        Fatal,
        Verbose
    }

    /// <summary>
    /// A minimal event that reprents a log message. optimised for space
    /// </summary>
    public class RLM : IRxn
    {
        /// <summary>
        /// The logmessage
        /// </summary>
        public string L { get; set; }
        /// <summary>
        /// The source of the logmessage
        /// </summary>
        public string S { get; set; }

        public override string ToString()
        {
            return L;
        }
    }

    /// <summary>
    /// Emitted by a portal/host when an inbound /events/publish (or equivalent
    /// cross-process Publish call) fails. Carries the *server-side* exception text
    /// so adapters and the portal UI can see why the host rejected the payload —
    /// otherwise the caller only sees an opaque HTTP 500 and the cause lives on
    /// the host's stdout / stderr where remote diagnosis can't reach it.
    ///
    /// Distinct from a generic logged error because it's the *one* event that
    /// records the inbound wire path failing — the precise gap the support portal
    /// is designed to close. Surfaces in /api/appstatus/errors via the standard
    /// LocalAppErrorManager subscription chain.
    /// </summary>
    public class PublishFailed : IRxn
    {
        /// <summary>Exception type name (e.g. RuntimeBinderException).</summary>
        public string ExceptionType { get; set; }
        /// <summary>Full exception message — typically the .Message of the outer ex.</summary>
        public string Message { get; set; }
        /// <summary>Inner exception details (type + message + stack), null when none.</summary>
        public string Inner { get; set; }
        /// <summary>Length in chars of the inbound payload that failed.</summary>
        public int PayloadLength { get; set; }
        /// <summary>First ~200 chars of the payload — enough to identify the wire shape.</summary>
        public string PayloadHead { get; set; }
        /// <summary>Caller's IP as seen by the host.</summary>
        public string ClientIp { get; set; }
        /// <summary>UTC timestamp the failure was observed.</summary>
        public DateTime Timestamp { get; set; }
    }

    public class LogMessage<T>
    {
        public DateTime Timestamp { get; set; }
        public string Reporter { get; set; }
        public T Message { get; set; }
        public LogLevel Level { get; set; }

        public LogMessage()
        {
            Timestamp = DateTime.Now;
        }

        public override string ToString()
        {
            return $"{(Level != LogLevel.None ? $"[{Thread.CurrentThread.Name.IsNullOrWhiteSpace($"{Environment.CurrentManagedThreadId:00}")}][{Timestamp:HH:mm:ss.ffff}][{Level}]" : "")}[{Reporter}] {(!Equals(Message, default(T)) ? Message.ToString() : "(null message logged)")}";
        }

        public IRxn ToRxn(string source = null)
        {
            return new RLM() { L = ToString(), S = source };
        }
    }
}
