using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rxns.DDD.BoundedContext
{
    public abstract class ChangeTrackingEntity
    {
        protected bool _trackChanges = true;
        protected List<IDomainEvent> Changes = new List<IDomainEvent>();

        protected virtual bool LogChange(IDomainEvent @change, int? index = null)
        {
            if (!_trackChanges) return false;

            if (index == null) Changes.Add(@change);
            else Changes.Insert(index.Value, @change);

            return true;
        }

        public virtual void MarkChangesAsCommitted(IEnumerable<IDomainEvent> changes = null)
        {
            if (changes == null)
            {
                Changes.Clear();
                return;
            }

            foreach (var c in changes.ToArray())
            {
                Changes.Remove(c);
            }
        }
    }
}
