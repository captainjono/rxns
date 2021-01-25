using System;
using Rxns.Interfaces;

namespace Rxns.Metrics
{
    public class TenantError : SystemRxn
    {
        public string Error { get; set; }
        public string StackTrace { get; set; }
    }

    public class SystemErrors : IRxn
    {
        public string ErrorId { get; set; }
        public DateTime Timestamp { get; set; }
        public string Tenant { get; set; }
        public string System { get; set; }
        public string Error { get; set; }
        public string StackTrace { get; set; }
        public bool Actioned { get; set; }
    }

    public class SystemLogMeta : IRxn
    {
        public long ErrorHistoryId { get; set; }
        public DateTime Timestamp { get; set; }
        public long ErrorId { get; set; }
        public string Level { get; set; }
        public string Reporter { get; set; }
        public string Message { get; set; }
    }
}
