using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rxns.DDD.BoundedContext
{
    public abstract class AggRoot : ChangeTrackingEntity, IAggRoot
    {
        public string EId { get; set; }
        public int Version { get; set; }
        public string Tenant { get; set; }

        public IEnumerable<IDomainEvent> GetUncommittedChanges()
        {
            return Changes.Select(c =>
            {
                c.AssignTenant(Tenant);
                return c;
            });
        }

        public void MarkChangesAsCommitted(params IDomainEvent[] changes)
        {
            foreach (var c in changes)
            {
                Changes.Remove(c);
            }
        }

        public void LoadFromHistory(IEnumerable<IDomainEvent> history)
        {
            try
            {
                _trackChanges = false;
                foreach (var domainEvent in history)
                {
                    if (Tenant != domainEvent.Tenant) throw new Exception("Cannot apply events from another tenant this this entity");
                    ApplyChange(domainEvent);
                }
            }
            finally
            {
                _trackChanges = true;
            }
        }

        public virtual void ApplyChange(dynamic @event)
        {
            try
            {
                ((dynamic)this).ApplyChange(@event);

                LogChange(@event);
            }
            catch (Exception e)
            {
                throw new Exception(String.Format("Cannot apply event history because handler void Apply({0}) threw an exception or was found on {1}", @event.GetType().Name, this), e);
            }
        }
    }
}
