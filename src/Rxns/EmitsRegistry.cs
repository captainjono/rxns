using System;
using System.Collections.Generic;
using System.Linq;
using Rxns.Logging;

namespace Rxns
{
    /// <summary>
    /// Tracks the IRxn types declared by <see cref="Hosting.IRxnLifecycle.Emits{T}"/>
    /// calls so <see cref="DistributedBackingChannel.ForEmits"/> can build its
    /// central-routing list from the actual emit declarations rather than a
    /// hand-curated <c>For(typeof(X), typeof(Y), ...)</c> arg list.
    ///
    /// Single source of truth: a component declares the events it emits
    /// cross-process via <c>.Emits&lt;T&gt;()</c>; those types are exactly the
    /// ones that get routed to <see cref="Interfaces.IRxnManagerRegistry.RxnsCentral"/>.
    /// Adding a new central-routable event becomes a one-line change instead
    /// of "add to Emits AND remember to update DistributedBackingChannel.For".
    ///
    /// <c>EmitsAnyIn&lt;T&gt;()</c> intentionally does NOT populate this
    /// registry — it's a DI bulk-registration helper ("any of these types
    /// might appear in the assembly"), not an emit-cross-process declaration.
    /// Auto-populating from it would over-approve status/meta events with
    /// non-serialisable Func closures that break JSON round-trip on Redis.
    /// </summary>
    public class EmitsRegistry
    {
        private readonly object _gate = new object();
        private readonly HashSet<Type> _types = new HashSet<Type>();

        public void Register(Type t)
        {
            if (t == null) return;
            lock (_gate) _types.Add(t);
        }

        public void RegisterAll(IEnumerable<Type> ts)
        {
            if (ts == null) return;
            lock (_gate) foreach (var t in ts) if (t != null) _types.Add(t);
        }

        public Type[] All()
        {
            lock (_gate) return _types.ToArray();
        }

        public bool Contains(Type t)
        {
            if (t == null) return false;
            lock (_gate) return _types.Any(x => x.IsAssignableFrom(t));
        }
    }
}
