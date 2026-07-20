using System.Collections.Generic;
using Rxns.DDD.BoundedContext;

namespace Rxns.DDD.Tenant
{
    public interface ITenantModelFactory<T>
        where T : IAggRoot, new()
    {
        T Create(string tenant, string id, IEnumerable<IDomainEvent> fromEvents);
        T Create(string tenant, string id);
    }

}
