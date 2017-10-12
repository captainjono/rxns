using System;

namespace Rxns
{
    public class MonitorAction<T> : IMonitorAction<T>
    {
        public Func<T, bool> When { get; set; }
        public Action<T> Do { get; set; }
    }
}
