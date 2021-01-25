using System;
using System.Reactive.Subjects;
using Autofac;
using Autofac.Core;

namespace Rxns.Autofac
{
    /// <summary>
    /// This provides real-time alerts to interested parties when specific types are resolved from a container, providing the specific instance
    /// as a variable to the Resolved sequence.
    /// </summary>
    /// <typeparam name="T">The type to watch</typeparam>
    public class OnTypeResolved<T> : Module where T : class
    {
        private readonly Subject<T> _resolved = new Subject<T>();
        public IObservable<T> Resolved { get { return _resolved; } }
        
        protected override void AttachToComponentRegistration(IComponentRegistry componentRegistry, IComponentRegistration registration)
        {
            registration.Activated += (sender, e) =>
            {
                if (typeof (T).IsAssignableFrom(e.Instance.GetType()))
                    _resolved.OnNext(e.Instance as T);
            };
        }

    }

    public class OnTypeResolved : Module 
    {
        private readonly Subject<object> _resolved = new Subject<object>();
        private Type _targetType;
        public IObservable<object> Resolved { get { return _resolved; } }

        public OnTypeResolved(Type targetType)
        {
            _targetType = targetType;
        }

        protected override void AttachToComponentRegistration(IComponentRegistry componentRegistry, IComponentRegistration registration)
        {
            registration.Activated += (sender, e) =>
            {
                if (_targetType.IsAssignableFrom(e.Instance.GetType()))
                    _resolved.OnNext(e.Instance);
            };
        }

    }
}
