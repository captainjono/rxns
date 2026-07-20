using System;
using System.Collections.Generic;
using System.Web.Http.Dependencies;
using Rxns.Interfaces;

namespace Rxns.WebApi.MsWebApiAdapters
{
    public class RxnxDependencyResolver : IDependencyResolver
    {
        private readonly IResolveTypes _resolver;

        public RxnxDependencyResolver(IResolveTypes resolver)
        {
            _resolver = resolver;
        }

        public void Dispose()
        {
            _resolver.Dispose();
        }

        public object GetService(Type serviceType)
        {
            return _resolver.Resolve(serviceType);
        }

        public IEnumerable<object> GetServices(Type serviceType)
        {
            return _resolver.Resolve(typeof(IEnumerable<>).MakeGenericType(serviceType)) as IEnumerable<object>;
        }

        public IDependencyScope BeginScope()
        {
            return new RxnxDependencyResolver(_resolver.BegingScope());
        }
    }
}
