using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Rxns.Collections
{
    public class InMemoryCacheFactory : ICacheFactory
    {
        public IDictionary<TKey, TValue> Create<TKey, TValue>(string dictionaryName = null)
        {
            return new UseConcurrentReliableOpsWhenCastToIDictionary<TKey, TValue>(new ConcurrentDictionary<TKey, TValue>());
        }
    }
}
