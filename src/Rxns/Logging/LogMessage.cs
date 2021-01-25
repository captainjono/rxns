using System;
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
            return $"{(Level != LogLevel.None ? $"[{Environment.CurrentManagedThreadId:00}][{Timestamp:HH:mm:ss.ffff}][{Level}]" : "")}[{Reporter}] {(!Equals(Message, default(T)) ? Message.ToString() : "(null message logged)")}";
        }

        public IRxn ToRxn(string source = null)
        {
            return new RLM() { L = ToString(), S = source };
        }
    }
}
