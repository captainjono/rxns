using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Rxns.Interfaces;

namespace Rxns.Logging
{
    public class ErrorReport
    {
        public DateTime Timestamp { get; set; }
        public string Tenant { get; set; }
        public string System { get; set; }

        public LogMessage<Exception> Error { get; set; }
        public IEnumerable<LogMessage<string>> History { get; set; }

        public override string ToString()
        {
            var sb = new StringBuilder();

            sb.AppendFormat("::Background::\r\nTenant: {0}\r\nSystem: {1}\r\nComponent: {2}\r\n", Tenant, System, Error.Reporter);
            sb.AppendFormat("::Error::\r\n{0}\r\n{1}", Error, Error.Message.StackTrace);

            if (History != null && History.Any())
            {
                sb.AppendFormat("\r\n::History::\r\n");

                foreach (var record in History)
                    sb.AppendFormat("{0}\r\n", record);
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// An ErrorReport that can be reliably serlised as sometimes
    /// Exceptions objects can cause problems!
    /// </summary>
    public class BasicErrorReport
    {
        public string Error { get; set; }
        public string StackTrace { get; set; }
        public string Tenant { get; set; }
        public string System { get; set; }
        public DateTime Timestamp { get; set; }
        public string Reporter { get; set; }

        public IEnumerable<LogMessage<string>> History { get; set; }

        public override string ToString()
        {
            var sb = new StringBuilder();

            sb.AppendFormat("::Background::\r\nTenant: {0}\r\nSystem: {1}\r\nComponent: {2}\r\n", Tenant, System, Reporter);
            sb.AppendFormat("::Error::\r\n{0}\r\n{1}", Error, StackTrace);

            if (History != null && History.Any())
            {
                sb.AppendFormat("\r\n::History::\r\n");

                foreach (var record in History)
                    sb.AppendFormat("{0}\r\n", record);
            }

            return sb.ToString();
        }
    }
}
