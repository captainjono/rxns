using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reactive;
using Rxns.Interfaces;

namespace Rxns.Collections
{
    public class DictionaryKeyValueStore<TKey, TValue> : IKeyValueStore<TKey, TValue> 
        where TValue : new()
    {
        private readonly IDictionary<TKey, TValue> _store;
        
        public DictionaryKeyValueStore(IDictionary<TKey, TValue> store = null)
        {
            _store = store ?? new UseConcurrentReliableOpsWhenCastToIDictionary<TKey, TValue>(new ConcurrentDictionary<TKey, TValue>());
        }

        public IObservable<TValue> Get(TKey key)
        {
            if (!_store.ContainsKey(key))
                _store.Add(key, new TValue());

            return _store[key].ToObservable();
        }

        public IObservable<bool> AddOrUpdate(TKey key, TValue value)
        {
            return _store.AddOrReplace(key, value).ToObservable();
        }

        public IObservable<bool> Exists(TKey key)
        {
            return _store.ContainsKey(key).ToObservable();
        }

        public IObservable<Unit> Clear()
        {
            _store.Clear();

            return new Unit().ToObservable();
        }


        public IObservable<Unit> Remove(TKey key)
        {
            _store.Remove(key);

            return new Unit().ToObservable();
        }

        public IObservable<IDictionary<TKey, TValue>> GetAll()
        {
            return _store.ToObservable();
        }
    }
}
