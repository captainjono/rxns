using System;
using Rxns.DDD.BoundedContext;
using Rxns.Interfaces;

namespace RxnsDemo.AzureB2C.Rxns
{
    public class TenantStatusChangedEvent : DomainEvent
    {
        public bool IsActive { get; private set; }

        public TenantStatusChangedEvent(string tenant, bool isActive) : base(tenant)
        {
            IsActive = isActive;
        }

        public TenantStatusChangedEvent(bool isActive)
        {
            IsActive = isActive;
        }
    }

    public interface ICurrentTenantsService: IRxnProcessor<TenantStatusChangedEvent>
    {
        IObservable<string[]> ActiveTenants { get; }
    }
}
