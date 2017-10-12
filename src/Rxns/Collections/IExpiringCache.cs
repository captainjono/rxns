using System;

namespace Rxns.Collections
{
    public interface IExpiringCache<TKey, TValue>
    {
        IObservable<TValue> GetOrLookup(TKey value, Func<TKey, TValue> lookupFunc);
        IObservable<TValue> GetOrLookup(TKey key, Func<TKey, IObservable<TValue>> lookupFunc);
        TValue Get(TKey key);
        void Set(TKey key, TValue value);
        bool Contains(TKey key);
        void Remove(TKey key);

        void CleanCache(DateTime withExpireyBefore);

    }
}
