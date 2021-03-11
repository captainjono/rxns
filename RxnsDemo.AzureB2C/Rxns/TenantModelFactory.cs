using System.Collections.Generic;
using Rxns.DDD.BoundedContext;

namespace RxnsDemo.AzureB2C.Rxns
{
    public class TenantModelFactory<T> : Tenant.ITenantModelFactory<T> where T : IAggRoot, new()
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
