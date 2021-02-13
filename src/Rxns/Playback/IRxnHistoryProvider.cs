using System;
using System.Collections.Generic;
using System.Linq;
using Rxns.DDD.BoundedContext;

namespace Rxns.Playback
{
    /// <summary>
    /// The aggregate used to store and retreive all events that have ever passed through the system
    /// </summary>
    public class EventHistory : IAggRoot
    {
        public string Tenant { get; set; }
        public string UnqiueId { get; set; }

        public int Version { get; set; }

        public void ApplyChange(dynamic @event)
        {
        }

        public string Data { get; private set; }

        private readonly string _filenumber;

        private const string GuidFormatForTimePortion = "yyyyMMdd-HHmm-ssff";

        public IEnumerable<IDomainEvent> History
        {
            get { return _historicalEvents.Concat(_newEvents); }
        }

        private readonly List<IDomainEvent> _historicalEvents;
        private List<IDomainEvent> _newEvents;
        private bool _isNewEvent = true;

        public EventHistory()
        {
            _historicalEvents = new List<IDomainEvent>();
            _newEvents = new List<IDomainEvent>();
        }

        public EventHistory(string tenant, string fileNumber) : this()
        {
            Tenant = tenant;
            _filenumber = fileNumber;

            // Get sequential id to achieve unique, sortable, timebased Id generation
            UnqiueId = GetTimeBasedGuid().ToString();
        }

        public string EId
        {
            // If agregate id is specified with '|' separator then the left part will be 'Partition Key' & the right part will be prefix for 'Row Key' in table,
            // so as to implement virtual partitions in code side (See more https://github.com/yevhen/Streamstone)
            get
            {
                if (Tenant.IsNullOrWhitespace() || UnqiueId.IsNullOrWhitespace()) throw new NullReferenceException("Tenant & EId should not be null or empty to store events");

                return string.Format("{0}|{1}|{2}", Tenant, UnqiueId);
            }
            set { }
        }

        public IEnumerable<IDomainEvent> GetUncommittedChanges()
        {
            return _newEvents;
        }

        public void MarkChangesAsCommitted(params IDomainEvent[] changes)
        {
            _newEvents = _newEvents.Except(changes).ToList();
        }

        public void LogEvents(params IDomainEvent[] events)
        {
            if (_isNewEvent) _newEvents.AddRange(events);
            else _historicalEvents.AddRange(events);

            Version += events.Length;
        }

        public void LoadFromHistory(IEnumerable<IDomainEvent> history)
        {
            _isNewEvent = false;
            Apply(history);
            _isNewEvent = true;
        }

        public void Apply(IEnumerable<IDomainEvent> history)
        {
            var allEvents = history.ToArray();
            LogEvents(allEvents);
        }


        public override bool Equals(object obj)
        {
            if (obj is EventHistory)
            {
                return (obj as EventHistory).UnqiueId == UnqiueId;
            }

            return base.Equals(obj);
        }

        protected bool Equals(EventHistory other)
        {
            return string.Equals(UnqiueId, other.UnqiueId);
        }

        public override int GetHashCode()
        {
            return (UnqiueId != null ? UnqiueId.GetHashCode() : 0);
        }

        /// <summary>
        /// Get guid which is generated based on server time
        /// </summary>
        /// <returns></returns>
        private static Guid GetTimeBasedGuid()
        {
            var guidPrefixForTime = DateTime.Now.ToString(GuidFormatForTimePortion);
            var guidPostfix = Guid.NewGuid().ToString().Substring(guidPrefixForTime.Length);
            return new Guid(string.Format("{0}{1}", guidPrefixForTime, guidPostfix));
        }
    }
}
