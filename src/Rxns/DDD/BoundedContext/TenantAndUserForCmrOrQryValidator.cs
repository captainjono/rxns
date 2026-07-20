using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Text;
using System.Threading.Tasks;
using Rxns.DDD.Commanding;
using Rxns.DDD.CQRS;
using Rxns.Interfaces;

namespace Rxns.DDD.BoundedContext
{


    public class UserDomainEvent : DomainEvent, IRequireUserContext
    {
        public string UserName { get; set; }

        public UserDomainEvent() { }

        public UserDomainEvent(string tenant, string userName) : base(tenant)
        {
            UserName = userName;
        }

        public bool HasUserSpecified()
        {
            return !UserName.IsNullOrWhitespace();
        }

        public void ForUser(string userName)
        {
            UserName = userName;
        }
    }
    
    public static class TenantDomainEventExtensions
    {
        public static T ForTenant<T>(this T context, string tenant) where T : IRequireTenantContext
        {
            context.AssignTenant(tenant);

            return context;
        }
    }

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

    public interface ICurrentTenantsService : IRxnProcessor<TenantStatusChangedEvent>
    {
        IObservable<string[]> ActiveTenants { get; }
    }

    public class TenantAndUserForCmrOrQryValidator<T> : IDomainCommandPreHandler<T> where T : class
    {
        private readonly ICurrentTenantsService _tenants;

        public TenantAndUserForCmrOrQryValidator(ICurrentTenantsService tenants)
        {
            _tenants = tenants;
        }

        public void Handle(T command)
        {
            if (command is IRequireTenantContext)
            {
                var cmd = command as IRequireTenantContext;

                if (!cmd.HasTenantSpecified()) ThrowCommandException((dynamic)command);
                if (!_tenants.ActiveTenants.Value().Contains(cmd.Tenant)) throw new UnknownTenantException(cmd.Tenant);
            }

            if (command is IRequireUserContext)
            {
                var cmd = command as IRequireUserContext;

                if (!cmd.HasUserSpecified()) ThrowCommandException((dynamic)command);
            }
        }

        private void ThrowCommandException<TT>(IDomainQuery<TT> query)
        {
            throw new DomainQueryException(query, "Tenant or UserName not specified on command");
        }

        private void ThrowCommandException<TT>(IDomainCommand<TT> command)
        {
            throw new DomainCommandException(command, "Tenant or UserName not specified on command");
        }
    }

    public class UnknownTenantException : Exception
    {
        public UnknownTenantException(string tenant)
            : base("Tenant '{0}' is either unknown or not currently active".FormatWith(tenant))
        {

        }
    }
}
