using System;
using Rxns.Interfaces;

namespace Rxns.Metrics
{
    public class SystemVersion : IRxn
    {
        public long Id { get; set; }

        public string Name { get; set; }
        public string Version { get; set; }
        public DateTime Timestamp { get; set; }

        public SystemVersion()
        {
            Timestamp = DateTime.Now;
        }
    }
}
