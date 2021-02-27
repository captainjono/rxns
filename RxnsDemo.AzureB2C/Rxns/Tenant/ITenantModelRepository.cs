using System.Collections.Generic;
using Rxns.DDD.BoundedContext;

namespace RxnsDemo.AzureB2C.Rxns
{

    public interface ITenantModelRepository<TAggregate>
        where TAggregate : IAggRoot, new()
    {
        TAggregate GetById(string tenant, string id);
        IEnumerable<IDomainEvent> Save(string tenant, TAggregate entity);
        void Save(string tenant, TAggregate entity, IEnumerable<IDomainEvent> events);
    }
}
