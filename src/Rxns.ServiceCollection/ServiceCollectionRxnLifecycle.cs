// ServiceCollectionRxnLifecycle - Native Microsoft.Extensions.DependencyInjection adapter for IRxnLifecycle
//
// This adapter allows Rxns modules to register services directly into an IServiceCollection,
// removing the need for Autofac in ASP.NET Core / .NET apps.
//
// Usage:
//   var services = new ServiceCollection(); // or from ASP.NET Core's builder.Services
//   var lifecycle = new ServiceCollectionRxnLifecycle(services);
//
//   // Use it like any IRxnLifecycle:
//   lifecycle.CreatesOncePerApp<MyService>();
//   lifecycle.CreatesOncePerAppAs<MyService, IMyService>();
//   lifecycle.Includes<MyModule>();
//
//   // In ASP.NET Core Startup / Program.cs:
//   builder.Services.AddRxns(lifecycle => {
//       lifecycle.CreatesOncePerApp<MyService>();
//       lifecycle.Includes<MyModule>();
//   });
//
//   // After all registrations, build the provider:
//   var provider = services.BuildServiceProvider();
//
//   // To bridge IResolveTypes for Rxns components that need it:
//   services.AddSingleton<IResolveTypes>(sp => new ServiceProviderTypeResolver(sp));

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rxns;
using Rxns.DDD.Commanding;
using Rxns.DDD.CQRS;
using Rxns.Hosting;
using Rxns.Interfaces;
using Rxns.Microservices;
using Rxns.Scheduling;

namespace Rxns.Delivery.Infrastructure
{
    /// <summary>
    /// Bridges IResolveTypes to IServiceProvider, so Rxns factory delegates that take
    /// IResolveTypes can resolve services from the MS DI container.
    /// </summary>
    public class ServiceProviderTypeResolver : IResolveTypes
    {
        private readonly IServiceProvider _sp;

        public ServiceProviderTypeResolver(IServiceProvider sp)
        {
            _sp = sp ?? throw new ArgumentNullException(nameof(sp));
        }

        public T Resolve<T>(params Tuple<string, object>[] parameters)
        {
            // MS DI does not support passing runtime parameters during resolution.
            // For the common case (no parameters), this works fine.
            return _sp.GetRequiredService<T>();
        }

        public T ResolveTag<T>(string named)
        {
            return _sp.GetRequiredKeyedService<T>(named);
        }

        public object Resolve(Type type)
        {
            return _sp.GetService(type);
        }

        public object Resolve(string typeName)
        {
            // Attempt to locate the type by name from the current AppDomain.
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a =>
                {
                    try { return a.GetTypes(); }
                    catch { return Array.Empty<Type>(); }
                })
                .FirstOrDefault(t => string.Equals(t.FullName, typeName.Split(',')[0], StringComparison.OrdinalIgnoreCase));

            if (type == null)
                throw new TypeLoadException($"Cannot locate '{typeName}' in loaded assemblies.");

            return _sp.GetService(type);
        }

        public IResolveTypes BegingScope()
        {
            var scope = _sp.CreateScope();
            return new ServiceProviderTypeResolver(scope.ServiceProvider);
        }

        public object ResolveOptional(Type serviceType)
        {
            return _sp.GetService(serviceType);
        }

        public void Dispose()
        {
            // The root IServiceProvider is typically owned by the host; do not dispose it here.
        }
    }

    /// <summary>
    /// Implements IRxnLifecycle using Microsoft.Extensions.DependencyInjection.IServiceCollection.
    /// Drop-in replacement for AutofacRxnLifecycle -- same IRxnLifecycle interface, no Autofac dependency.
    /// </summary>
    public class ServiceCollectionRxnLifecycle : IRxnLifecycle
    {
        private readonly IServiceCollection _services;
        // Per-lifecycle EmitsRegistry, populated by Emits<>()/EmitsAnyIn<>() and
        // resolved at DI build time by DistributedBackingChannel.ForEmits().
        private readonly EmitsRegistry _emits = new EmitsRegistry();

        public ServiceCollectionRxnLifecycle(IServiceCollection services)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _services.AddSingleton(_emits);
        }

        /// <summary>
        /// The underlying IServiceCollection, exposed for advanced scenarios.
        /// </summary>
        public IServiceCollection Services => _services;

        // ─── Singleton (once-per-app) registrations ───────────────────────

        public IRxnLifecycle CreatesOncePerApp<T>(bool preserveExisting = false)
        {
            var implType = typeof(T);
            if (preserveExisting)
            {
                _services.TryAddSingleton(implType, implType);
                foreach (var iface in implType.GetInterfaces())
                    _services.TryAddSingleton(iface, sp => sp.GetRequiredService(implType));
            }
            else
            {
                _services.AddSingleton(implType, implType);
                foreach (var iface in implType.GetInterfaces())
                    _services.AddSingleton(iface, sp => sp.GetRequiredService(implType));
            }
            return this;
        }

        public IRxnLifecycle CreatesOncePerAppAs<TService, TImplements>(bool preserveExisting = false)
        {
            var implType = typeof(TService);
            var serviceType = typeof(TImplements);

            if (preserveExisting)
            {
                _services.TryAddSingleton(implType, implType);
                _services.TryAddSingleton(serviceType, sp => sp.GetRequiredService(implType));
            }
            else
            {
                _services.AddSingleton(implType, implType);
                _services.AddSingleton(serviceType, sp => sp.GetRequiredService(implType));
            }
            return this;
        }

        public IRxnLifecycle CreatesOncePerApp(Type type)
        {
            _services.AddSingleton(type, type);
            foreach (var iface in type.GetInterfaces())
                _services.AddSingleton(iface, sp => sp.GetRequiredService(type));
            return this;
        }

        public IRxnLifecycle CreateGenericOncePerAppAs(Type type, Type asT)
        {
            _services.AddSingleton(asT, type);
            return this;
        }

        public IRxnLifecycle CreatesOncePerApp<T>(Func<T> factory, bool preserveExisting = false, string named = null)
        {
            var implType = typeof(T);

            if (preserveExisting)
            {
                _services.TryAddSingleton(implType, sp => factory());
                foreach (var iface in implType.GetInterfaces())
                    _services.TryAddSingleton(iface, sp => sp.GetRequiredService(implType));
            }
            else
            {
                _services.AddSingleton(implType, sp => factory());
                foreach (var iface in implType.GetInterfaces())
                    _services.AddSingleton(iface, sp => sp.GetRequiredService(implType));
            }

            if (!string.IsNullOrWhiteSpace(named))
                _services.AddKeyedSingleton(typeof(T), named, (sp, _) => sp.GetRequiredService<T>());

            return this;
        }

        public IRxnLifecycle CreatesOncePerApp<T>(Func<IResolveTypes, T> factory, bool preserveExisting = false, params string[] named)
        {
            var implType = typeof(T);

            if (preserveExisting)
            {
                _services.TryAddSingleton(implType, sp => factory(GetResolver(sp)));
                foreach (var iface in implType.GetInterfaces())
                    _services.TryAddSingleton(iface, sp => sp.GetRequiredService(implType));
            }
            else
            {
                _services.AddSingleton(implType, sp => factory(GetResolver(sp)));
                foreach (var iface in implType.GetInterfaces())
                    _services.AddSingleton(iface, sp => sp.GetRequiredService(implType));
            }

            if (named != null)
            {
                foreach (var name in named)
                {
                    if (!string.IsNullOrWhiteSpace(name))
                        _services.AddKeyedSingleton(typeof(T), name, (sp, _) => sp.GetRequiredService<T>());
                }
            }

            return this;
        }

        // ─── Named singleton registrations ────────────────────────────────

        public IRxnLifecycle CreatesOncePerAppNamed<T, TName>(string name = null, bool preserveExisting = false)
        {
            var implType = typeof(T);
            var key = name ?? implType.Name;

            if (preserveExisting)
            {
                _services.TryAddSingleton(implType, implType);
                foreach (var iface in implType.GetInterfaces())
                    _services.TryAddSingleton(iface, sp => sp.GetRequiredService(implType));
            }
            else
            {
                _services.AddSingleton(implType, implType);
                foreach (var iface in implType.GetInterfaces())
                    _services.AddSingleton(iface, sp => sp.GetRequiredService(implType));
            }

            _services.AddKeyedSingleton(typeof(TName), key, (sp, _) =>sp.GetRequiredService(implType));

            return this;
        }

        public IRxnLifecycle CreatesOncePerAppNamed<T, TName>(Func<T> factory, string name, bool preserveExisting = false)
        {
            var implType = typeof(T);
            var key = name ?? implType.Name;

            if (preserveExisting)
                _services.TryAddSingleton(implType, sp => factory());
            else
                _services.AddSingleton(implType, sp => factory());

            _services.AddKeyedSingleton(typeof(TName), key, (sp, _) =>sp.GetRequiredService(implType));

            return this;
        }

        // ─── Scoped / per-request registrations ──────────────────────────

        public IRxnLifecycle CreatesOncePerRequest<T>()
        {
            var implType = typeof(T);
            _services.AddTransient(implType, implType);
            foreach (var iface in implType.GetInterfaces())
                _services.AddTransient(iface, implType);
            return this;
        }

        public IRxnLifecycle CreatesOncePerRequest<T>(Func<T> factory)
        {
            var implType = typeof(T);
            _services.AddTransient(implType, sp => factory());
            foreach (var iface in implType.GetInterfaces())
                _services.AddTransient(iface, sp => factory());
            return this;
        }

        public IRxnLifecycle CreatesOncePerRequest<T>(Func<IResolveTypes, T> factory)
        {
            var implType = typeof(T);
            _services.AddTransient(implType, sp => factory(GetResolver(sp)));
            foreach (var iface in implType.GetInterfaces())
                _services.AddTransient(iface, sp => factory(GetResolver(sp)));
            return this;
        }

        public IRxnLifecycle CreatesOncePerRequestAs<T>(Func<IResolveTypes, IEnumerable<object>, T> factory)
        {
            _services.AddTransient(typeof(T), sp => factory(GetResolver(sp), Enumerable.Empty<object>()));
            return this;
        }

        public IRxnLifecycle CreatesOncePerRequestAs<T, T2>(Func<IResolveTypes, IEnumerable<object>, T> factory)
        {
            var implType = typeof(T);
            var serviceType2 = typeof(T2);

            _services.AddTransient(implType, sp => factory(GetResolver(sp), Enumerable.Empty<object>()));
            _services.AddTransient(serviceType2, sp => sp.GetRequiredService(implType));
            return this;
        }

        public IRxnLifecycle CreatesOncePerRequestNamed<T, TName>(string name = null, bool preserveExisting = false)
        {
            var implType = typeof(T);
            var key = name ?? implType.Name;

            if (preserveExisting)
            {
                _services.TryAddTransient(implType, implType);
                foreach (var iface in implType.GetInterfaces())
                    _services.TryAddTransient(iface, implType);
            }
            else
            {
                _services.AddTransient(implType, implType);
                foreach (var iface in implType.GetInterfaces())
                    _services.AddTransient(iface, implType);
            }

            _services.AddKeyedTransient(typeof(TName), key, (sp, _) =>sp.GetRequiredService(implType));

            return this;
        }

        // ─── Event / command registrations ────────────────────────────────

        public IRxnLifecycle RespondsToCmd<T>() where T : IDomainCommand
        {
            // Register the event type itself as transient (like RegisterEvent<T>)
            _services.AddTransient(typeof(T), typeof(T));
            // Register as IServiceCommand with a keyed registration for lookup by type name
            _services.AddTransient(typeof(IServiceCommand), typeof(T));
            _services.AddKeyedTransient(typeof(IServiceCommand), typeof(T).FullName, typeof(T));
            return this;
        }

        public IRxnLifecycle RespondsToQry<T>() where T : IDomainQuery
        {
            _services.AddTransient(typeof(T), typeof(T));
            _services.AddTransient(typeof(IServiceCommand), typeof(T));
            _services.AddKeyedTransient(typeof(IServiceCommand), typeof(T).FullName, typeof(T));
            return this;
        }

        public IRxnLifecycle RespondsToSvcCmds<T>() where T : IServiceCommand
        {
            // Register all IServiceCommand implementations from the assembly containing T
            foreach (var type in typeof(T).GetTypeInfo().Assembly.GetTypes()
                .Where(t => typeof(IServiceCommand).IsAssignableFrom(t) && !t.GetTypeInfo().IsAbstract && t.GetTypeInfo().IsClass))
            {
                _services.AddTransient(typeof(IServiceCommand), type);
                _services.AddKeyedTransient(typeof(IServiceCommand), type.Name.ToLower(), type);
            }
            return this;
        }

        public IRxnLifecycle RunsTask<T>() where T : ITask
        {
            // Register all SchedulableTask implementations from the assembly containing T
            foreach (var type in typeof(T).GetTypeInfo().Assembly.GetTypes()
                .Where(t => typeof(SchedulableTask).IsAssignableFrom(t) && !t.GetTypeInfo().IsAbstract && t.GetTypeInfo().IsClass))
            {
                _services.AddTransient(type, type);
                foreach (var iface in type.GetInterfaces())
                    _services.AddTransient(iface, type);
                _services.AddKeyedTransient(typeof(ISchedulableTask), type.Name, type);
            }
            return this;
        }

        public IRxnLifecycle Emits<T>() where T : IRxn
        {
            _services.AddTransient(typeof(T), typeof(T));
            _emits.Register(typeof(T));
            return this;
        }

        public IRxnLifecycle EmitsAnyIn<T>() where T : IRxn
        {
            var types = typeof(T).GetTypeInfo().Assembly.GetTypes()
                .Where(t => typeof(IRxn).IsAssignableFrom(t) && !t.GetTypeInfo().IsAbstract && t.GetTypeInfo().IsClass)
                .ToList();
            foreach (var type in types)
            {
                _services.AddTransient(type, type);
            }
            // Intentional: EmitsAnyIn does NOT populate EmitsRegistry. See the
            // matching comment in AutofacRxnLifecycle for rationale.
            return this;
        }

        // ─── Module inclusion ─────────────────────────────────────────────

        public IRxnLifecycle Includes<T>() where T : IAppModule, new()
        {
            var module = new T();
            module.Load(this);
            return this;
        }

        // ─── Helpers ──────────────────────────────────────────────────────

        private static IResolveTypes GetResolver(IServiceProvider sp)
        {
            // Try to get an already-registered IResolveTypes; otherwise create an ad-hoc one.
            var existing = sp.GetService<IResolveTypes>();
            return existing ?? new ServiceProviderTypeResolver(sp);
        }
    }

    /// <summary>
    /// Extension methods for integrating ServiceCollectionRxnLifecycle with ASP.NET Core's
    /// IServiceCollection in Startup/Program.cs.
    /// </summary>
    public static class ServiceCollectionRxnExtensions
    {
        /// <summary>
        /// Configures Rxns services using the native MS DI container.
        /// Registers IResolveTypes so Rxns components that depend on it work correctly.
        /// </summary>
        /// <param name="services">The service collection to populate.</param>
        /// <param name="configure">Action that receives an IRxnLifecycle for fluent registration.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddRxns(this IServiceCollection services, Action<IRxnLifecycle> configure)
        {
            // Ensure IResolveTypes is registered so factory delegates can bridge to MS DI.
            services.TryAddSingleton<IResolveTypes>(sp => new ServiceProviderTypeResolver(sp));

            var lifecycle = new ServiceCollectionRxnLifecycle(services);
            configure(lifecycle);

            return services;
        }

        /// <summary>
        /// Loads an Rxns module into the service collection.
        /// </summary>
        public static IServiceCollection AddRxnModule<TModule>(this IServiceCollection services)
            where TModule : IAppModule, new()
        {
            services.TryAddSingleton<IResolveTypes>(sp => new ServiceProviderTypeResolver(sp));

            var lifecycle = new ServiceCollectionRxnLifecycle(services);
            var module = new TModule();
            module.Load(lifecycle);

            return services;
        }
    }
}
