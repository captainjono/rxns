using System;

namespace Rxns.Health
{
    public class QueueSnapshotEvent : HealthEvent
    {
        public TimeSpan ProcessTime { get; private set; }
        public long TotalItems { get; private set; }

        public QueueSnapshotEvent() { }

        public QueueSnapshotEvent(string name, TimeSpan processTime, long totalItems)
            : base(name)
        {
            ProcessTime = processTime;
            TotalItems = totalItems;
        }
    }
}
