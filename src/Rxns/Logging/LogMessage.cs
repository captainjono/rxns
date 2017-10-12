using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rxns.Interfaces
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
            return String.Format("[{4:00}][{0:HH:mm:ss.ffff}][{1}][{2}] {3}", Timestamp, Level, Reporter, !Equals(Message, default(T)) ? Message.ToString() : "(null message logged)", Environment.CurrentManagedThreadId);
        }
    }
}
