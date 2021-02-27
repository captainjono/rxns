using System.Collections.Generic;
using Rxns.DDD.BoundedContext;

namespace RxnsDemo.AzureB2C.Rxns
{
    public interface ITenantModelFactory<T>
        where T : IAggRoot, new()
    {
        T Create(string tenant, string id, IEnumerable<IDomainEvent> fromEvents);
        T Create(string tenant, string id);
    }

}
