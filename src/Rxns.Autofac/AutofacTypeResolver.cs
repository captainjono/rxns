using System;
using System.Collections.Generic;
using System.Linq;
using Autofac;
using Rxns.DDD.Commanding;
using Rxns.Hosting;
using Rxns.Interfaces;

namespace Rxns.Autofac
{
    public class AutofacTypeResolver : AutofacComponentContextResolver, IServiceCommandFactory, IListKnownTypes, ITaskFactory
    {
        private readonly Func<ILifetimeScope> _createScope;
        private readonly ILifetimeScope _resolver;
        private readonly IContainer _container;

        public IEnumerable<Type> Services => _container.ComponentRegistry.Registrations.Select(r => r.Activator.LimitType);

        public AutofacTypeResolver(IContainer container) : base(container)
        {
            _createScope = () => container.BeginLifetimeScope();
            _resolver = container.Resolve<ILifetimeScope>();
            _container = container;
        }

        public AutofacTypeResolver(Func<ILifetimeScope> createScope, ILifetimeScope resolver) : base(resolver)
        {
            _createScope = createScope;
            _resolver = resolver;
        }

        public T Get<T>(string taskName) where T : IReportStatus
        {
            var lifeTime = _createScope().BeginLifetimeScope();
            var task = lifeTime.ResolveNamed<T>(taskName);
            lifeTime.DisposedBy(task);

            return task;
        }

        public object Get(string typeFullname)
        {
            return _resolver.Resolve<object>(typeFullname);
        }

        public IServiceCommand Get(string cmdName, params object[] constructorParams)
        {
            //needs to be an array because at various points autofac will enumerate  this list and cause the 
            //index to become out of order
            var i = 0;
            var @params = constructorParams.Select(p => new PositionalParameter(i++, p)).ToArray();
            try
            {
                return _resolver.ResolveNamed<IServiceCommand>(cmdName, @params);

            }
            catch (Exception e)
            {
                throw new Exception($"Unknown command: {cmdName}");
            }
        }

        public object Get(string typeFullname, params string[] constructorParams)
        {//needs to be an array because at various points autofac will enumerate  this list and cause the 
            //index to become out of order
            var i = 0;
            var @params = constructorParams.Select(p => new PositionalParameter(i++, p)).ToArray();
            return _resolver.Resolve<object>(typeFullname, @params);
        }

         
    }
}
