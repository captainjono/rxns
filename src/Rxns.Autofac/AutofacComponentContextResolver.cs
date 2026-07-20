using System;
using System.Linq;
using Autofac;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns.Autofac
{
    public class AutofacComponentContextResolver : ReportsStatus, IResolveTypes
    {
        private readonly IComponentContext _resolver;

        public AutofacComponentContextResolver(IComponentContext resolver)
        {
            _resolver = resolver;
        }

        public T Resolve<T>(params Tuple<string, object>[] parameters)
        {
            return _resolver.Resolve<T>(parameters.Select(p => new NamedParameter(p.Item1, p.Item2)));
        }

        public T ResolveTag<T>(string named)
        {
            return _resolver.ResolveNamed<T>(named);
        }

        public object Resolve(Type type)
        {
            return !_resolver.IsRegistered(type)
                ? null :
                _resolver.Resolve(type);
        }

        public object Resolve(string typeName)
        {
            return _resolver.Resolve<object>(typeName);
        }

        public IResolveTypes BegingScope()
        {
            return new AutofacComponentContextResolver(_resolver.Resolve<ILifetimeScope>());
        }

        public object ResolveOptional(Type typeName)
        {
            return _resolver.ResolveOptional(typeName);
        }
    }
}
