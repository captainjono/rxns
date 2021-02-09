//using System;
//using System.Collections.Generic;
//using System.Diagnostics.CodeAnalysis;
//using System.Reflection;
//using System.Threading.Tasks;
//using Autofac;
//using Autofac.Builder;
//using Autofac.Core;
//using Autofac.Core.Registration;
//using Microsoft.Extensions.DependencyInjection;
//using Rxns.Autofac;
//using Rxns.Interfaces;
//using Rxns.Logging;

//namespace Rxns.WebApiNET5.NET5WebApiAdapters.RxnsApiAdapters
//{
//    /// <summary>
//    /// Autofac implementation of the ASP.NET Core <see cref="IServiceScope"/>.
//    /// </summary>
//    /// <seealso cref="Microsoft.Extensions.DependencyInjection.IServiceScope" />
//    internal class AutofacServiceScope : IServiceScope, IAsyncDisposable


//    {
//        private bool _disposed;
//        private readonly AutofacTypeResolver _serviceProvider;

//        /// <summary>
//        /// Initializes a new instance of the <see cref="AutofacServiceScope"/> class.
//        /// </summary>
//        /// <param name="lifetimeScope">
//        /// The lifetime scope from which services should be resolved for this service scope.
//        /// </param>
//        public AutofacServiceScope(ILifetimeScope lifetimeScope)
//        {
//            this._serviceProvider = new AutofacTypeResolver(() => lifetimeScope.BeginLifetimeScope(), lifetimeScope);
//        }

//        /// <summary>
//        /// Gets an <see cref="IServiceProvider" /> corresponding to this service scope.
//        /// </summary>
//        /// <value>
//        /// An <see cref="IServiceProvider" /> that can be used to resolve dependencies from the scope.
//        /// </value>
//        public IServiceProvider ServiceProvider
//        {
//            get
//            {
//                return this._serviceProvider;
//            }
//        }

//        /// <summary>
//        /// Disposes of the lifetime scope and resolved disposable services.
//        /// </summary>
//        public void Dispose()
//        {
//            this.Dispose(true);
//            GC.SuppressFinalize(this);
//        }

//        /// <summary>
//        /// Releases unmanaged and - optionally - managed resources.
//        /// </summary>
//        /// <param name="disposing">
//        /// <see langword="true" /> to release both managed and unmanaged resources; <see langword="false" /> to release only unmanaged resources.
//        /// </param>
//        protected virtual void Dispose(bool disposing)
//        {
//            if (!this._disposed)
//            {
//                this._disposed = true;

//                if (disposing)
//                {
//                    this._serviceProvider.Dispose();
//                }
//            }
//        }

//        public async ValueTask DisposeAsync()
//        {
//            if (!this._disposed)
//            {
//                this._disposed = true;
//                this._serviceProvider.Dispose();
//            }
//        }
//    }

//    /// <summary>
//    /// Autofac implementation of the ASP.NET Core <see cref="IServiceScopeFactory"/>.
//    /// </summary>
//    /// <seealso cref="Microsoft.Extensions.DependencyInjection.IServiceScopeFactory" />
//    [SuppressMessage("Microsoft.ApiDesignGuidelines", "CA2213", Justification = "The creator of the root service lifetime scope is responsible for disposal.")]
//        internal class AutofacServiceScopeFactory : IServiceScopeFactory
//        {
//            private readonly ILifetimeScope _lifetimeScope;

//            /// <summary>
//            /// Initializes a new instance of the <see cref="AutofacServiceScopeFactory"/> class.
//            /// </summary>
//            /// <param name="lifetimeScope">The lifetime scope.</param>
//            public AutofacServiceScopeFactory(ILifetimeScope lifetimeScope)
//            {
//                this._lifetimeScope = lifetimeScope;
//            }

//            /// <summary>
//            /// Creates an <see cref="IServiceScope" /> which contains an
//            /// <see cref="System.IServiceProvider" /> used to resolve dependencies within
//            /// the scope.
//            /// </summary>
//            /// <returns>
//            /// An <see cref="IServiceScope" /> controlling the lifetime of the scope. Once
//            /// this is disposed, any scoped services that have been resolved
//            /// from the <see cref="IServiceScope.ServiceProvider" />
//            /// will also be disposed.
//            /// </returns>
//            public IServiceScope CreateScope()
//            {
//                return new AutofacServiceScope(this._lifetimeScope.BeginLifetimeScope());
//            }
        
//    }

//    /// <summary>
//    /// Extension methods for registering ASP.NET Core dependencies with Autofac.
//    /// </summary>
//    public static class AutofacRegistration
//        {
//            /// <summary>
//            /// Populates the Autofac container builder with the set of registered service descriptors
//            /// and makes <see cref="IServiceProvider"/> and <see cref="IServiceScopeFactory"/>
//            /// available in the container.
//            /// </summary>
//            /// <param name="builder">
//            /// The <see cref="ContainerBuilder"/> into which the registrations should be made.
//            /// </param>
//            /// <param name="descriptors">
//            /// The set of service descriptors to register in the container.
//            /// </param>
//            public static void Populate(
//                this ContainerBuilder builder,
//                IEnumerable<ServiceDescriptor> descriptors)
//            {
//                Populate(builder, descriptors, null);
//            }

//            /// <summary>
//            /// Populates the Autofac container builder with the set of registered service descriptors
//            /// and makes <see cref="IServiceProvider"/> and <see cref="IServiceScopeFactory"/>
//            /// available in the container. Using this overload is incompatible with the ASP.NET Core
//            /// support for <see cref="IServiceProviderFactory{TContainerBuilder}"/>.
//            /// </summary>
//            /// <param name="builder">
//            /// The <see cref="ContainerBuilder"/> into which the registrations should be made.
//            /// </param>
//            /// <param name="descriptors">
//            /// The set of service descriptors to register in the container.
//            /// </param>
//            /// <param name="lifetimeScopeTagForSingletons">
//            /// If provided and not <see langword="null"/> then all registrations with lifetime <see cref="ServiceLifetime.Singleton" /> are registered
//            /// using <see cref="IRegistrationBuilder{TLimit,TActivatorData,TRegistrationStyle}.InstancePerMatchingLifetimeScope" />
//            /// with provided <paramref name="lifetimeScopeTagForSingletons"/>
//            /// instead of using <see cref="IRegistrationBuilder{TLimit,TActivatorData,TRegistrationStyle}.SingleInstance"/>.
//            /// </param>
//            /// <remarks>
//            /// <para>
//            /// Specifying a <paramref name="lifetimeScopeTagForSingletons"/> addresses a specific case where you have
//            /// an application that uses Autofac but where you need to isolate a set of services in a child scope. For example,
//            /// if you have a large application that self-hosts ASP.NET Core items, you may want to isolate the ASP.NET
//            /// Core registrations in a child lifetime scope so they don't show up for the rest of the application.
//            /// This overload allows that. Note it is the developer's responsibility to execute this and create an
//            /// <see cref="AutofacServiceProvider"/> using the child lifetime scope.
//            /// </para>
//            /// </remarks>
//            public static void Populate(
//                this ContainerBuilder builder,
//                IEnumerable<ServiceDescriptor> descriptors,
//                object lifetimeScopeTagForSingletons)
//            {
//                if (descriptors == null)
//                {
//                    throw new ArgumentNullException(nameof(descriptors));
//                }

//                builder.RegisterType<RxnxDependencyResolver>().As<IServiceProvider>().ExternallyOwned();
//                builder.RegisterType<AutofacServiceScopeFactory>().As<IServiceScopeFactory>();

//                Register(builder, descriptors, lifetimeScopeTagForSingletons);
//            }

//            /// <summary>
//            /// Configures the lifecycle on a service registration.
//            /// </summary>
//            /// <typeparam name="TActivatorData">The activator data type.</typeparam>
//            /// <typeparam name="TRegistrationStyle">The object registration style.</typeparam>
//            /// <param name="registrationBuilder">The registration being built.</param>
//            /// <param name="lifecycleKind">The lifecycle specified on the service registration.</param>
//            /// <param name="lifetimeScopeTagForSingleton">
//            /// If not <see langword="null"/> then all registrations with lifetime <see cref="ServiceLifetime.Singleton" /> are registered
//            /// using <see cref="IRegistrationBuilder{TLimit,TActivatorData,TRegistrationStyle}.InstancePerMatchingLifetimeScope" />
//            /// with provided <paramref name="lifetimeScopeTagForSingleton"/>
//            /// instead of using <see cref="IRegistrationBuilder{TLimit,TActivatorData,TRegistrationStyle}.SingleInstance"/>.
//            /// </param>
//            /// <returns>
//            /// The <paramref name="registrationBuilder" />, configured with the proper lifetime scope,
//            /// and available for additional configuration.
//            /// </returns>
//            private static IRegistrationBuilder<object, TActivatorData, TRegistrationStyle> ConfigureLifecycle<TActivatorData, TRegistrationStyle>(
//                this IRegistrationBuilder<object, TActivatorData, TRegistrationStyle> registrationBuilder,
//                ServiceLifetime lifecycleKind,
//                object lifetimeScopeTagForSingleton)
//            {
//                switch (lifecycleKind)
//                {
//                    case ServiceLifetime.Singleton:
//                        if (lifetimeScopeTagForSingleton == null)
//                        {
//                            registrationBuilder.SingleInstance();
//                        }
//                        else
//                        {
//                            registrationBuilder.InstancePerMatchingLifetimeScope(lifetimeScopeTagForSingleton);
//                        }

//                        break;
//                    case ServiceLifetime.Scoped:
//                        registrationBuilder.InstancePerLifetimeScope();
//                        break;
//                    case ServiceLifetime.Transient:
//                        registrationBuilder.InstancePerDependency();
//                        break;
//                }

//                return registrationBuilder;
//            }

//            /// <summary>
//            /// Populates the Autofac container builder with the set of registered service descriptors.
//            /// </summary>
//            /// <param name="builder">
//            /// The <see cref="ContainerBuilder"/> into which the registrations should be made.
//            /// </param>
//            /// <param name="descriptors">
//            /// The set of service descriptors to register in the container.
//            /// </param>
//            /// <param name="lifetimeScopeTagForSingletons">
//            /// If not <see langword="null"/> then all registrations with lifetime <see cref="ServiceLifetime.Singleton" /> are registered
//            /// using <see cref="IRegistrationBuilder{TLimit,TActivatorData,TRegistrationStyle}.InstancePerMatchingLifetimeScope" />
//            /// with provided <paramref name="lifetimeScopeTagForSingletons"/>
//            /// instead of using <see cref="IRegistrationBuilder{TLimit,TActivatorData,TRegistrationStyle}.SingleInstance"/>.
//            /// </param>
//            private static void Register(
//                ContainerBuilder builder,
//                IEnumerable<ServiceDescriptor> descriptors,
//                object lifetimeScopeTagForSingletons)
//            {
//                foreach (var descriptor in descriptors)
//                {
//                    if (descriptor.ImplementationType != null)
//                    {
//                        // Test if the an open generic type is being registered
//                        var serviceTypeInfo = descriptor.ServiceType.GetTypeInfo();
//                        if (serviceTypeInfo.IsGenericTypeDefinition)
//                        {
//                            builder
//                                .RegisterGeneric(descriptor.ImplementationType)
//                                .As(descriptor.ServiceType)
//                                .ConfigureLifecycle(descriptor.Lifetime, lifetimeScopeTagForSingletons);
//                        }
//                        else
//                        {
//                            builder
//                                .RegisterType(descriptor.ImplementationType)
//                                .As(descriptor.ServiceType)
//                                .ConfigureLifecycle(descriptor.Lifetime, lifetimeScopeTagForSingletons);
//                        }
//                    }
//                    else if (descriptor.ImplementationFactory != null)
//                    {
//                        var registration = RegistrationBuilder.ForDelegate(descriptor.ServiceType, (context, parameters) =>
//                        {
//                            var serviceProvider = context.Resolve<IServiceProvider>();
//                            return descriptor.ImplementationFactory(serviceProvider);
//                        })
//                            .ConfigureLifecycle(descriptor.Lifetime, lifetimeScopeTagForSingletons)
//                            .CreateRegistration();

//                        builder.RegisterComponent(registration);
//                    }
//                    else
//                    {
//                        builder
//                            .RegisterInstance(descriptor.ImplementationInstance)
//                            .As(descriptor.ServiceType)
//                            .ConfigureLifecycle(descriptor.Lifetime, null);
//                    }
//                }
//            }
//        }
//    }



//    public class RxnxDependencyResolver : IServiceProvider, ISupportRequiredService, IDisposable, IAsyncDisposable
//    {
//        private readonly IResolveTypes _resolver;

//        public RxnxDependencyResolver(IResolveTypes resolver)
//        {
//            _resolver = resolver;
//        }
        
//        private bool _disposed = false;
        
//        /// <summary>
//        /// Gets service of type <paramref name="serviceType" /> from the
//        /// <see cref="AutofacServiceProvider" /> and requires it be present.
//        /// </summary>
//        /// <param name="serviceType">
//        /// An object that specifies the type of service object to get.
//        /// </param>
//        /// <returns>
//        /// A service object of type <paramref name="serviceType" />.
//        /// </returns>
//        /// <exception cref="ComponentNotRegisteredException">
//        /// Thrown if the <paramref name="serviceType" /> isn't registered with the container.
//        /// </exception>
//        /// <exception cref="DependencyResolutionException">
//        /// Thrown if the object can't be resolved from the container.
//        /// </exception>
//        public object GetRequiredService(Type serviceType)
//        {
//            return _resolver.Resolve(serviceType);
//        }

//        /// <summary>
//        /// Gets the service object of the specified type.
//        /// </summary>
//        /// <param name="serviceType">
//        /// An object that specifies the type of service object to get.
//        /// </param>
//        /// <returns>
//        /// A service object of type <paramref name="serviceType" />; or <see langword="null" />
//        /// if there is no service object of type <paramref name="serviceType" />.
//        /// </returns>
//        public object GetService(Type serviceType)
//        {
//            return _resolver.ResolveOptional(serviceType);
//        }

//        /// <summary>
//        /// Releases unmanaged and - optionally - managed resources.
//        /// </summary>
//        /// <param name="disposing">
//        /// <see langword="true" /> to release both managed and unmanaged resources;
//        /// <see langword="false" /> to release only unmanaged resources.
//        /// </param>
//        protected virtual void Dispose(bool disposing)
//        {
//            "Dispose on resolver called".LogDebug();

//            if (!this._disposed)
//            {
//                this._disposed = true;
//                if (disposing)
//                {
//                    _resolver.Dispose();
//                }
//            }
//        }

//        /// <summary>
//        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
//        /// </summary>
//        public void Dispose()
//        {
//            this.Dispose(true);
//            GC.SuppressFinalize(this);
//        }

//        /// <summary>
//        /// Performs a dispose operation asynchronously.
//        /// </summary>
//        public async ValueTask DisposeAsync()
//        {
//            if (!this._disposed)
//            {
//                this._disposed = true;
//                _resolver.Dispose();
//                //await _resolver.DisposeAsync();
//                GC.SuppressFinalize(this);
//            }
//        }


//        public IEnumerable<object> GetServices(Type serviceType)
//        {
//            return _resolver.Resolve(typeof(IEnumerable<>).MakeGenericType(serviceType)) as IEnumerable<object>;
//        }
//    }

