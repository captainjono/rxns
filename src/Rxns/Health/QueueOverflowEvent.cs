namespace Rxns.Health
{
    public class QueueOverflowEvent : HealthEvent
    {
        public long TotalItems { get; set; }
        public long Threshold { get; set; }

        public QueueOverflowEvent() { }

        public QueueOverflowEvent(string reporterName, long totalItems)
            : base(reporterName)
        {
            TotalItems = totalItems;
        }
    }
}
