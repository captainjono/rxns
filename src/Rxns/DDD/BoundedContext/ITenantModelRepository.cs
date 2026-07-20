using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Newtonsoft.Json;
using Rxns.DDD.Tenant;
using Rxns.Interfaces;

namespace Rxns.DDD.BoundedContext
{
    public interface ITenantModelRepository<TAggregate>
        where TAggregate : IAggRoot, new()
    {
        IObservable<TAggregate> GetById(string tenant, string id);
        IEnumerable<IDomainEvent> Save(string tenant, TAggregate entity);
        void Save(string tenant, TAggregate entity, IEnumerable<IDomainEvent> events);
    }

    public class EventSourcingRepository
    {
        /// <summary>n
        /// This is a DTO class used by the event sourcing repo
        /// </summary>
        //[Alias("EventStore")]
        public class EventData
        {
            public int Id { get; set; }

            public DateTime Timestamp { get; set; }

            public string AggregteType { get; set; }

            public string AggregateId { get; set; }

            public int Version { get; set; }

            public string Event { get; set; }

            public string MetaData { get; set; }

            public string EventId { get; set; }

            public bool Dispatched { get; set; }

            public EventData()
            {

            }

            public EventData(IAggRoot agg, string @event, string id)
                : this()
            {
                AggregteType = agg.GetType().FullName;
                AggregateId = agg.EId;
                Version = agg.Version;
                Timestamp = DateTime.Now;
                Event = @event;
                EventId = id;
            }
        }
    }
    public interface IReactiveEventRepository
    {
        IObservable<EventsOccured> Changes { get; }
    }

    public class EventsOccured
    {
        public IEnumerable<IDomainEvent> Events { get; set; }
        public string Id { get; set; }
        public string Tenant { get; set; }
    }

    public interface IStoreFactory
    {
        IEnumerable<EventSourcingRepository.EventData> ExistsById(string id);
        IEnumerable<EventSourcingRepository.EventData> GetById(string id);

        void InsertBulk(string id, IEnumerable<EventSourcingRepository.EventData> data);
    }

    public class EventSourcingRepository<T, TStore> : EventSourcingRepository, ITenantModelRepository<T> where T : IAggRoot, new() where TStore : IStoreFactory
    {
        private readonly Func<string, TStore> _storeFactory;
        private readonly ITenantModelFactory<T> _dmFactory;
        private readonly IDictionary<string, TStore> _storeCache = new Dictionary<string, TStore>();
        private readonly JsonSerializerSettings jsonSettings;
        private readonly IResolveTypes _type;
        private readonly Subject<EventsOccured> _changes = new Subject<EventsOccured>();
        private readonly IScheduler _changesScheduler;

        public IObservable<EventsOccured> Changes
        {
            get { return _changes.SubscribeOn(_changesScheduler).ObserveOn(_changesScheduler); } //we dont want changes subscriptions to block save
        }
        public EventSourcingRepository(ITenantModelFactory<T> dmFactory, Func<string, TStore> storeFactory, IResolveTypes type, IScheduler changesScheduler = null)
        {
            _storeFactory = storeFactory;
            _dmFactory = dmFactory;
            _type = type;
            jsonSettings = new JsonSerializerSettings();
            
            jsonSettings.NullValueHandling = NullValueHandling.Ignore; //compress the data we store so empty fields arnt saved
            _changesScheduler = changesScheduler ?? TaskPoolScheduler.Default;
            //jsonSettings.Converters.Add(new UseTFromJsonCreationConverter());
        }

        private readonly static object _singleThread = new object();
        protected TStore GetContextFor(string tenant)
        {
            lock (_singleThread)
            {
                if (_storeCache.ContainsKey(tenant)) return _storeCache[tenant];

                var context = _storeFactory(tenant);
                _storeCache.Add(tenant, context);

                return context;
            }
        }
        public IEnumerable<IDomainEvent> FromEventData(IEnumerable<EventData> data)
        {
            return data.Select(e =>
            {
                var evt = e.Event.Deserialise(e.Event.GetTypeFromJson(_type));
                return (IDomainEvent)evt;
            });
        }


        public IObservable<T> GetById(string tenant, string id)
        {
            return Rxn.Create(() =>
            {
                var events = GetContextFor(tenant).GetById(id).OrderBy(e => e.Id);

                if (!events.AnyItems()) return _dmFactory.Create(tenant, id);
                var model = _dmFactory.Create(tenant, id, FromEventData(events));

                return model;
            });
        }

        public IEnumerable<IDomainEvent> Save(string tenant, T entity)
        {
            var uncommitted = entity.GetUncommittedChanges().ToArray();
            Save(tenant, entity, uncommitted);
            entity.MarkChangesAsCommitted(uncommitted);

            _changes.OnNext(new EventsOccured()
            {
                Id = entity.EId,
                Tenant = tenant,
                Events = uncommitted
            });

            return uncommitted;
        }

        public void Save(string tenant, T entity, IEnumerable<IDomainEvent> uncommitted)
        {
            var changes = new List<IDomainEvent>();
            var context = GetContextFor(tenant);
            //entity.ThrowValidationExceptions(); //ensure entity is valid before saving

            context.InsertBulk(entity.EId, uncommitted.Select(@event =>
            {
                changes.Add(@event);
                return AsEventData(entity, @event, @event.Id);
            }));

            entity.MarkChangesAsCommitted(null);

            _changes.OnNext(new EventsOccured()
            {
                Id = entity.EId,
                Tenant = tenant,
                Events = changes
            });
        }

        private EventData AsEventData(IAggRoot agg, IDomainEvent @event, string id)
        {
            var serilised = @event.Serialise().ResolveAs(@event.GetType());
            var data = new EventData(agg, serilised, id);

            return data;
        }

    }
}
