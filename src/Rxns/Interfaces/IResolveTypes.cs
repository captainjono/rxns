using System;

namespace Rxns.Interfaces
{

    public interface IResolveTypes : IDisposable
    {
        T Resolve<T>(params Tuple<string, object>[] parameters);
        T ResolveTag<T>(string named);
        object Resolve(Type type);
        object Resolve(string typeName);
        IResolveTypes BegingScope();
    }
}
