namespace Rxns.Health
{
    public class QueueSpeedEvent : HealthEvent
    {
        public long TotalItems { get; private set; }

        public QueueSpeedEvent() { }

        public QueueSpeedEvent(string name, long totalItems)
            : base(name)
        {
            TotalItems = totalItems;
        }
    }
}
