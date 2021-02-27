using System;
using System.Linq;
using System.Reactive.Linq;
using System.Reflection;
using Autofac.Builder;
using Autofac.Core;
using Rxns.Interfaces;
using Rxns.DDD.Commanding;
using Rxns.Logging;
using Rxns.Scheduling;

namespace Autofac
{
    /// <summary>
    /// Some extensions for the autofac library
    /// </summary>
    public static class AutofacExtensions
    {
        /// <summary>
        /// Forces resolve of a single instance of T. Useful for services that are not dependend on by any other component
        /// but need to be instantiated in the system.
        /// </summary>
        /// <typeparam name="T">The type of the service.</typeparam>
        /// <param name="b">The container builder used to register the service.</param>
        public static IRegistrationBuilder<T, ConcreteReflectionActivatorData, SingleRegistrationStyle>
            RegisterAndActivate<T>(this ContainerBuilder b)
        {
            b.RegisterType<StartableBootstrap<T>>()
                .As<IStartable>()
                .SingleInstance();

            return b.RegisterType<T>().As<T>();
        }

        /// <summary>
        /// Forces resolve of a single instance of T. Useful for services that are not dependend on by any other component
        /// but need to be instantiated in the system.
        /// </summary>
        /// <param name="b">The container builder used to register the service.</param>
        /// <param name="c">A delegate that uses the context to create/resolve the component.</param>
        /// <returns></returns>
        public static IRegistrationBuilder<T, SimpleActivatorData, SingleRegistrationStyle> RegisterAndActivate<T>(
            this ContainerBuilder b, Func<IComponentContext, T> c)
        {
            b.RegisterType<StartableBootstrap<T>>()
                .As<IStartable>()
                .SingleInstance();

            return b.Register(c).As<T>();
        }

        /// <summary>
        /// Registers all IRxns from the assembly containing the specified type
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="cb"></param>
        /// <param name="c"></param>
        /// <returns></returns>
        public static void RegisterEvents<T>(this ContainerBuilder cb)
        {
            foreach (var type in typeof(T).GetTypeInfo().Assembly.GetTypes().Where(t => t.IsAssignableTo<IRxn>() && !t.GetTypeInfo().IsAbstract && t.GetTypeInfo().IsClass))
            {
                cb.RegisterType(type).AsSelf().InstancePerDependency();
            }
        }


        /// <summary>
        /// Registers all ScheduableTasks from the assembly containing the specified type so that they can be resolved
        /// via the container and as a named instance from json shorthand aka tasks.json
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="cb"></param>
        /// <param name="c"></param>
        /// <returns></returns>
        public static void RegisterTasks<T>(
            this ContainerBuilder cb)
        {

            //register all SchedulableTask's as they are created dynamically in various places from the container
            foreach (var type in typeof(T).GetTypeInfo().Assembly.GetTypes().Where(t => t.IsAssignableTo<SchedulableTask>() && !t.GetTypeInfo().IsAbstract && t.GetTypeInfo().IsClass))
            {
                cb.RegisterType(type).AsImplementedInterfaces().AsSelf().Named<ISchedulableTask>(type.Name).InstancePerDependency();
            }
        }

        public static void RegisterEvent<T>(this ContainerBuilder cb)
        {
            cb.RegisterType<T>().AsSelf().InstancePerDependency();
        }

        public static void RegisterEvents(this ContainerBuilder cb, Type assembly)
        {
            foreach (var type in assembly.GetTypeInfo().Assembly.GetTypes().Where(t => t.IsAssignableTo<IRxn>() && !t.GetTypeInfo().IsAbstract && t.GetTypeInfo().IsClass))
            {
                cb.RegisterType(type).AsSelf().InstancePerDependency();
            }
        }

        public static void RegisterServiceCommands<T>(this ContainerBuilder cb)
        {
            foreach (var type in typeof(T).GetTypeInfo().Assembly.GetTypes().Where(t => t.IsAssignableTo<IServiceCommand>() && !t.GetTypeInfo().IsAbstract && t.GetTypeInfo().IsClass))
            {
                cb.RegisterType(type).As<IServiceCommand>().Named<IServiceCommand>(type.Name).InstancePerDependency();
            }
        }

        public static T Resolve<T>(this IComponentContext context, string typeFullname, params Parameter[] parameters) where T : class
        {
            var type = (
                from reg in context.ComponentRegistry.Registrations
                from service in reg.Target.Services.OfType<TypedService>()
                    .Where(service => service.ServiceType.FullName.Equals(typeFullname.Split(',')[0], StringComparison.OrdinalIgnoreCase))
                select service.ServiceType).FirstOrDefault();

            if (type == null)
                throw new TypeLoadException(String.Format("Cannot locate '{0}' in container. Make sure it has been registered", typeFullname));

            return context.Resolve(type, parameters) as T;
        }

        /// <summary>
        ///     Used to force the container to activate a component once when the configuration has completed.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        internal class StartableBootstrap<T> : IStartable
        {
            private readonly IComponentContext _context;
            private readonly IAppEvents _systemEvents;

            public StartableBootstrap(IComponentContext context, IAppEvents systemEvents)
            {
                _context = context;
                _systemEvents = systemEvents;
            }

            public void Start()
            {
                _systemEvents.OnStart.FirstAsync().Subscribe(cb => _context.Resolve<T>());
            }
        }
    }
}
