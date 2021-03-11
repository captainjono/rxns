using System;
using System.Collections.Generic;
using Rxns.DDD.BoundedContext;

namespace RxnsDemo.AzureB2C.Rxns.Tenant
{
    public class InMemoryUserContext : IUserContext
    {
        public Guid Id { get; set; }
        public string UserName { get; set; }
        public string Tenant { get; set; }
        public string[] Roles { get; set; }
        public string Name { get; private set; }
        public string Email { get; private set; }

        public bool IsInternal { get; private set; }

        //public Lazy<AppClientAgg> Client { get; private set; }

        public IEnumerable<IDomainEvent> SaveChanges()
        {
            //var changes = Client.Value.GetUncommittedChanges().ToArray();
            //Client.Value.MarkChangesAsCommitted(changes);
            //return changes;

            return new IDomainEvent[0];
        }
    }
}
