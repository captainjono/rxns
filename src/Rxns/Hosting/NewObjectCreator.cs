using System;
using System.Collections.Generic;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns.Hosting
{
    public class NewObjectCreator : IResolveTypes
    {
        public NewObjectCreator()
        {
            "WARNING Resources will not be released using this resolver. You must track them yourself".LogDebug();
        }

        public IEnumerable<Type> Services => GetType().Assembly.ExportedTypes;

        public T Resolve<T>()
        {
            return (T)ResolveNamed(typeof(T).FullName);
        }

        public T Resolve<T>(params Tuple<string, object>[] parameters)
        {
            "Warning, parameter creation is not supported with this resolver".LogDebug();

            return Resolve<T>();
        }

        public T ResolveTag<T>(string named)
        {
            "Warning, named instances are not supported with this resolver".LogDebug();

            return (T)ResolveNamed(typeof(T).FullName);
        }

        public object Resolve(Type type)
        {
            return ResolveNamed(type.FullName);
        }

        public object Resolve(string typeName)
        {
            throw new NotImplementedException();
        }

        public object ResolveNamed(string typeName)
        {
            return Activator.CreateInstance(Type.GetType(typeName), typeName);
        }

        public IResolveTypes BegingScope()
        {
            "Scopes are not supported!".LogDebug();
            return new NewObjectCreator();
        }

        public object ResolveOptional(Type serviceType)
        {
            throw new NotImplementedException();
        }

        public void Dispose()
        {
            "WARNING Resources will not be released!".LogDebug();
        }
    }
}
