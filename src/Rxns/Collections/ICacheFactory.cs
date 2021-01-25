using System.Collections.Generic;

namespace Rxns.Collections
{

    public interface ICacheFactory
    {
        IDictionary<TKey, TValue> Create<TKey, TValue>(string dictionaryName = null);
    }
}
