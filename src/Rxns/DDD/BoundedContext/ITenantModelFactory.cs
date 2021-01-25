using System.Collections.Generic;

namespace Rxns.DDD.BoundedContext
{
    public interface ITenantModelFactory<T>
        where T : IAggRoot, new()
    {
        T Create(string tenant, string id, IEnumerable<IDomainEvent> fromEvents);
        T Create(string tenant, string id);
    }
}
