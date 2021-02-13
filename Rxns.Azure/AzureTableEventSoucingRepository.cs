//using System;
//using System.Collections.Generic;
//using System.IO;
//using System.Linq;
//using Microsoft.Extensions.Logging;
//using Microsoft.WindowsAzure.Storage.Table;
//using Newtonsoft.Json;
//using Rxns.DDD.BoundedContext;
//using Rxns.Interfaces;

//namespace Rxns.Azure
//{
//    /// <summary>
//    /// Repository class which uses uses streamstore to store aggregate event streams
//    /// </summary>
//    /// <typeparam name="T"></typeparam>
//    public class AzureTableEventSourcingRepository<T> : ITenantModelRepository<T>, IRxnHistoryProvider where T : IAggRoot, new() 
//    {
//        private readonly CloudTable _table;
//        private static IResolveTypes _type;
//        private JsonSerializerSettings _jsonSettings;
//        private const string GuidFormatForTimePortion = "yyyyMMdd-HHmm-ssff";

//        public AzureTableEventSourcingRepository(CloudTable table, IResolveTypes type)
//        {
//            _table = table;
//            _type = type;
//            _jsonSettings = new JsonSerializerSettings();
//        }

//        public T GetById(string tenant, string aggregateId)
//        {
//            var paritionKey = aggregateId;
//            var partition = new Partition(_table, paritionKey);

//            if (!Stream.Exists(partition))
//            {
//                throw new AggregateNotFoundException(aggregateId, "Agregate not found for the specified aggregate id");
//            }
//            var events = Stream.Read<EventEntity>(partition).Events;

//            //could also depend on ITenantModelFactory<T> dmFactor and use factory to create
//            //instance of agg. since this agg is only used internally to the class, its probably not 
//            //that important
//            //var agg = _dmFactory.Create(tenant, aggregateId, events.Select(ToEvent));

//            var agg = new T();
//            agg.LoadFromHistory(events.Select(ToEvent));
//            return agg;
//        }

//        public IEnumerable<IDomainEvent> Save(string tenant, T aggregate)
//        {
//            var uncomittedEvents = aggregate.GetUncommittedChanges().ToArray();
//            Save(tenant, aggregate, uncomittedEvents);
//            aggregate.MarkChangesAsCommitted(uncomittedEvents);

//            return uncomittedEvents;
//        }

//        public void Save(string tenant, T aggregate, IEnumerable<IDomainEvent> uncomittedEvents)
//        {
//            // Version
//            var i = 1;

//            // Disable since currently not using version
//            // Iterate through current aggregate events increasing version with each processed event
//            //foreach (var @event in events)
//            //{
//            //    i++;
//            //    @event.Version = i;
//            //}

//            var partitionKey = aggregate.EId;
//            var partition = new Partition(_table, partitionKey);
//            //var eventId = partitionKey.Contains("|") ? partition.RowKeyPrefix : new Guid().ToString();

//            var doesExist = Stream.TryOpen(partition);
//            var aggStore = doesExist.Found
//                ? doesExist.Stream
//                : new Stream(partition);

//            // Disable since currently not using version 
//            // if (stream.Version != expectedVersion)
//            //    throw new ConcurrencyException();

//            try
//            {
//                var events = uncomittedEvents.Select(ToEventData).ToArray();
//                Stream.Write(aggStore, events);
//            }
//            catch (ConcurrencyConflictException e)
//            {
//                throw new ConcurrencyException("Concurrency exception found among events");
//            }
//        }

//        /// <summary>
//        /// Get all events before the specified time by queying table since stream stone requires partition key, such as tenant, for read
//        /// </summary>
//        /// <param name="fromDate">The earliest event that is returned</param>
//        /// <param name="includeReactiveEvents">The history will contain  events that are produced in reaction to other events. Playing these back through the system will cause double-ups, so by default, they are not included</param>
//        /// <returns></returns>
//        public IEnumerable<IEvent> GetAll(DateTime? fromDate = null, bool includeReactiveEvents = false)
//        {
//            var query = new TableQuery<EventEntity>();
//            if (fromDate != null)
//                query = query.Where(TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.GreaterThanOrEqual, ((DateTime)fromDate).ToString(GuidFormatForTimePortion)));

//            var entities = _table.ExecuteQuery(query).Where(e => !string.IsNullOrEmpty(e.Type) && !string.IsNullOrEmpty(e.Data));
//            var history = entities.Select(ToEvent);
            
//            if(includeReactiveEvents)
//                return history;
            
//            return history.Where(e => !e.GetType().IsAssignableTo<IReactiveEvent>()); //only events that are not in reactive to another event, which would cause double ups on playback
//        }

//        private ITenantDomainEvent ToEvent(EventEntity e)
//        {
//            return (ITenantDomainEvent)e.Data.FromJson(e.Data.GetTypeFromJson(_type), _jsonSettings);
//        }

//        private static EventData ToEventData(ITenantDomainEvent e) 
//        {
//            var @event = e.ToJson().AttachTypeToJson(e.GetType());
//            var properties = new
//            {
//                Id = e.Id,
//                Type = e.GetType().FullName,
//                Data = @event
//            };

//            return new EventData(EventId.From(e.Id), EventProperties.From(properties));
//        }

//        private class EventEntity : TableEntity
//        {
//            public string Type { get; set; }
//            public string Data { get; set; }
//        }
//    }
//}
