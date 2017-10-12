using System;

namespace Rxns.Health
{
    public abstract class HealthEvent : IHealthEvent
    {
        public string ReporterName { get; private set; }

        public HealthEvent()
        {
            TimeCaptured = DateTime.Now;
        }

        public HealthEvent(string name)
            : this()
        {
            ReporterName = name;
        }

        public DateTime TimeCaptured { get; private set; }

        public IHealthEvent Tunnel(IReportHealth another)
        {
            ReporterName = "{0}.{1}".FormatWith(another.ReporterName, ReporterName);
            return this;
        }
    }
}
