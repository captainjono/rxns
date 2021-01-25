using System.Collections.Generic;

namespace Rxns.DDD.BoundedContext
{
    public class TenantModelFactory<T> : ITenantModelFactory<T> where T : IAggRoot, new()
    {
        public T Create(string tenant, string id, IEnumerable<IDomainEvent> fromEvents)
        {
            var model = new T();

            model.EId = id;
            model.Tenant = tenant;
            model.LoadFromHistory(fromEvents);

            return model;
        }

        public T Create(string tenant, string id)
        {
            var model = new T();

            model.Tenant = tenant;
            model.EId = id;

            return model;
        }
    }
}
