using System;
using System.Collections.Generic;
using System.Reactive;

namespace Rxns.Interfaces
{
    public interface IKeyValueStore<TKey, TValue> 
    {
        /// <summary>
        /// Returns the value if the key exist and has data, or deafult(TValue) if it doesnt. No need to explicitly call exists before this method
        /// </summary>
        /// <param name="key">The key to lookup a value for</param>
        /// <returns>The value or default(TValue)</returns>
        IObservable<TValue> Get(TKey key);
        IObservable<IDictionary<TKey, TValue>> GetAll();
        IObservable<bool> AddOrUpdate(TKey key, TValue value);
        IObservable<Unit> Remove(TKey key);
        IObservable<bool> Exists(TKey key);
        IObservable<Unit> Clear();
    }
}
