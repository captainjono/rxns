namespace RxnsDemo.AzureB2C.Rxns.Sql
{

    //public class SqlEventSourcingRepository
    //{
    //    /// <summary>n
    //    /// This is a DTO class used by the event sourcing repo
    //    /// </summary>
    //    [Alias("EventStore")]
    //    public class EventData
    //    {
    //        [Key]
    //        [AutoIncrement]
    //        public int Id { get; set; }

    //        public DateTime Timestamp { get; set; }

    //        [Required]
    //        public string AggregteType { get; set; }

    //        [Required]
    //        public string AggregateId { get; set; }

    //        [Required]
    //        public int Version { get; set; }

    //        [Required]
    //        public string Event { get; set; }

    //        public string MetaData { get; set; }

    //        public Guid EventId { get; set; }

    //        [Required]
    //        [DefaultValue(false)]
    //        public bool Dispatched { get; set; }

    //        public EventData()
    //        {

    //        }

    //        public EventData(IAggRoot agg, string @event, Guid id)
    //            : this()
    //        {
    //            AggregteType = agg.GetType().FullName;
    //            AggregateId = agg.EId;
    //            Version = agg.Version;
    //            Timestamp = DateTime.Now;
    //            Event = @event;
    //            EventId = id;
    //        }
    //    }
    //}
    //public interface IReactiveEventRepository
    //{
    //    IObservable<EventsOccured> Changes { get; }
    //}

    //public class EventsOccured
    //{
    //    public IEnumerable<IDomainEvent> Events { get; set; }
    //    public string Id { get; set; }
    //    public string Tenant { get; set; }
    //}
    //public class SqlEventSourcingRepository<T> : SqlEventSourcingRepository, ITenantModelRepository<T> where T : IAggRoot, new()
    //{
    //    private readonly ITenantDatabaseFactory _ormFactory;
    //    private readonly ITenantModelFactory<T> _dmFactory;
    //    private readonly IDictionary<string, IOrmContext> _ormCache = new Dictionary<string, IOrmContext>();
    //    private readonly JsonSerializerSettings jsonSettings;
    //    private readonly IResolveTypes _type;
    //    private readonly Subject<EventsOccured> _changes = new Subject<EventsOccured>();
    //    private readonly IScheduler _changesScheduler;

    //    public IObservable<EventsOccured> Changes
    //    {
    //        get { return _changes.SubscribeOn(_changesScheduler).ObserveOn(_changesScheduler); } //we dont want changes subscriptions to block save
    //    }
    //    public SqlEventSourcingRepository(ITenantModelFactory<T> dmFactory, ITenantDatabaseFactory ormFactory, IResolveTypes type, IScheduler changesScheduler = null)
    //    {
    //        _ormFactory = ormFactory;
    //        _dmFactory = dmFactory;
    //        _type = type;
    //        jsonSettings = new JsonSerializerSettings();
    //        //SerialisationService.DefaultSettings.CopyTo(jsonSettings);
    //        jsonSettings.NullValueHandling = NullValueHandling.Ignore; //compress the data we store so empty fields arnt saved
    //        _changesScheduler = changesScheduler ?? TaskPoolScheduler.Default;
    //    }

    //    private readonly static object _singleThread = new object();
    //    protected IOrmContext GetContextFor(string tenant)
    //    {
    //        lock (_singleThread)
    //        {
    //            if (_ormCache.ContainsKey(tenant)) return _ormCache[tenant];

    //            var context = _ormFactory.GetContext(tenant);
    //            context.Run(c => c.CreateTableIfNotExists<EventData>());
    //            _ormCache.Add(tenant, context);

    //            return context;
    //        }
    //    }
    //    public IEnumerable<IDomainEvent> FromEventData(IEnumerable<EventData> data)
    //    {
    //        return data.Select(e =>
    //        {
    //            var evt = e.Event.FromJson(e.Event.GetTypeFromJson(_type), jsonSettings);
    //            return (IDomainEvent)evt;
    //        });
    //    }


    //    public T GetById(string tenant, string id)
    //    {
    //        var events = GetContextFor(tenant).Run(c => c.Query<EventData>(e => e.Where(w => w.AggregateId == id && w.AggregteType == typeof(T).FullName)).OrderBy(e => e.Id));

    //        if (!events.AnyItems()) return _dmFactory.Create(tenant, id);
    //        var model = _dmFactory.Create(tenant, id, FromEventData(events));

    //        return model;
    //    }

    //    public T ExistsById(string tenant, string id)
    //    {
    //        var events = GetContextFor(tenant).Run(c => c.Query<EventData>(e => e.Where(w => w.AggregateId == id && w.AggregteType == typeof(T).FullName)));

    //        if (!events.AnyItems()) return _dmFactory.Create(tenant, id);
    //        var model = _dmFactory.Create(tenant, id, FromEventData(events));

    //        return model;
    //    }


    //    public IEnumerable<IDomainEvent> Save(string tenant, T entity)
    //    {
    //        var uncommitted = entity.GetUncommittedChanges().ToArray();
    //        Save(tenant, entity, uncommitted);
    //        entity.MarkChangesAsCommitted(uncommitted);

    //        _changes.OnNext(new EventsOccured()
    //        {
    //            Id = entity.EId,
    //            Tenant = tenant,
    //            Events = uncommitted
    //        });

    //        return uncommitted;
    //    }

    //    /// <summary>
    //    /// was just changing this so it was fully ienumerable so we get better load on sql throughout the parsing process ?
    //    /// or should it commit the transaction ASAP ?
    //    /// </summary>
    //    /// <param name="tenant"></param>
    //    /// <param name="entity"></param>
    //    /// <param name="uncommitted"></param>
    //    public void Save(string tenant, T entity, IEnumerable<IDomainEvent> uncommitted)
    //    {
    //        var changes = new List<IDomainEvent>();
    //        var context = GetContextFor(tenant);
            
    //        context.Run(c => c.InsertBulk(uncommitted.Select(@event =>
    //        {
    //            changes.Add(@event);
    //            return AsEventData(entity, @event, @event.Id);
    //        })));

    //        entity.MarkChangesAsCommitted(null);

    //        _changes.OnNext(new EventsOccured()
    //        {
    //            Id = entity.EId,
    //            Tenant = tenant,
    //            Events = changes
    //        });
    //    }

    //    private EventData AsEventData(IAggRoot agg, IDomainEvent @event, Guid id)
    //    {
    //        var serilised = @event.ToJson().ResolveAs(@event.GetType());
    //        var data = new EventData(agg, serilised, id);

    //        return data;
    //    }

    //}


    //public class LegacySqlEventSourcingRepository
    //{
    //    /// <summary>n
    //    /// This is a DTO class used by the event sourcing repo
    //    /// </summary>
    //    [Alias("EventStore")]
    //    public class EventData
    //    {
    //        [Key]
    //        [AutoIncrement]
    //        public int Id { get; set; }

    //        public DateTime Timestamp { get; set; }

    //        [Required]
    //        public string AggregteType { get; set; }

    //        [Required]
    //        public string AggregateId { get; set; }

    //        [Required]
    //        public int Version { get; set; }

    //        [Required]
    //        public string Event { get; set; }

    //        public string MetaData { get; set; }

    //        [Required]
    //        [DefaultValue(false)]
    //        public bool Dispatched { get; set; }

    //        public EventData()
    //        {

    //        }

    //        public EventData(IAggRoot agg, string @event)
    //            : this()
    //        {
    //            AggregteType = agg.GetType().FullName;
    //            AggregateId = agg.EId;
    //            Version = agg.Version;
    //            Timestamp = DateTime.Now;
    //            Event = @event;
    //        }
    //    }
    //}

    //public class LegacySqlEventSourcingRepository<T> : SqlEventSourcingRepository, ITenantModelRepository<T> where T : IAggRoot, new()
    //{
    //    private readonly ITenantDatabaseFactory _ormFactory;
    //    private readonly ITenantModelFactory<T> _dmFactory;
    //    private readonly IDictionary<string, IOrmContext> _ormCache = new Dictionary<string, IOrmContext>();
    //    private readonly JsonSerializerSettings jsonSettings;
    //    private readonly IResolveTypes _type;
    //    private readonly Subject<EventsOccured> _changes = new Subject<EventsOccured>();
    //    private readonly IScheduler _changesScheduler;

    //    public IObservable<EventsOccured> Changes
    //    {
    //        get { return _changes.SubscribeOn(_changesScheduler).ObserveOn(_changesScheduler); } //we dont want changes subscriptions to block save
    //    }
    //    public LegacySqlEventSourcingRepository(ITenantModelFactory<T> dmFactory, ITenantDatabaseFactory ormFactory, IResolveTypes type, IScheduler changesScheduler = null)
    //    {
    //        _ormFactory = ormFactory;
    //        _dmFactory = dmFactory;
    //        _type = type;
    //        jsonSettings = new JsonSerializerSettings();
    //        jsonSettings.NullValueHandling = NullValueHandling.Ignore; //compress the data we store so empty fields arnt saved
    //        _changesScheduler = changesScheduler ?? TaskPoolScheduler.Default;
    //        //jsonSettings.Converters.Add(new UseTFromJsonCreationConverter());
    //    }

    //    public T GetById(string tenant, string id)
    //    {
    //        throw new NotImplementedException();
    //    }

    //    public IEnumerable<IDomainEvent> Save(string tenant, T entity)
    //    {
    //        throw new NotImplementedException();
    //    }

    //    public void Save(string tenant, T entity, IEnumerable<IDomainEvent> events)
    //    {
    //        throw new NotImplementedException();
    //    }
    //}
}
