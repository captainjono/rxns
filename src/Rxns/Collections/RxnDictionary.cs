using System;
using System.Collections;
using System.Collections.Generic;

namespace Rxns.Collections
{
    /// <summary>
    /// A very thin event sourcing wrapper for a dictionary that complies with the 
    /// IDictionary interface. A great way to have your cake, and eat it too!
    /// A good applications includes updating an inmemory cache of a domain model, 
    /// no need to query the store, just stream evnets in.
    /// 
    /// "I want to map an rxn to a specific dictionary operation"
    /// </summary>
    /// <typeparam name="TEvents">The base type of the rxn stream</typeparam>
    /// <typeparam name="TKey">The dictionary key</typeparam>
    /// <typeparam name="TValue">The dictionary value</typeparam>
    public class RxnDictionary<TEvents, TKey, TValue> : IDictionary<TKey, TValue>, IDisposable
    {
        private readonly IDictionary<TKey, TValue> _bd;
        
        /// <summary>
        /// exposes the reaction decorator that drives the underlieing changes
        /// </summary>
        public readonly RxnDecorator<TEvents, IDictionary<TKey, TValue>> RxnDecorator;

        /// <summary>
        /// Augments a dictionary with an rxn sources flavouring
        /// </summary>
        /// <param name="backingDictionary">The dictionary that will be wrapped and rxn sauced</param>
        /// <param name="events">The rxn stream which will mutate the dictionary</param>
        /// <param name="transformations">The transforms that map each rxn to a dictionary operation</param>
        public RxnDictionary(IDictionary<TKey, TValue> backingDictionary, IObservable<TEvents> @events, params Action<TEvents, IDictionary<TKey, TValue>>[] transformations)
        {
            _bd = backingDictionary;
            RxnDecorator = new RxnDecorator<TEvents, IDictionary<TKey, TValue>>(this, @events, transformations);
        }

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
        {
            return _bd.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public void Add(KeyValuePair<TKey, TValue> item)
        {
            _bd.Add(item);
        }

        public void Clear()
        {
            _bd.Clear();
        }

        public bool Contains(KeyValuePair<TKey, TValue> item)
        {
            return _bd.Contains(item);
        }

        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        {
            _bd.CopyTo(array, arrayIndex);
        }

        public bool Remove(KeyValuePair<TKey, TValue> item)
        {
            return _bd.Remove(item);
        }

        public int Count { get { return _bd.Count; } }
        public bool IsReadOnly { get { return _bd.IsReadOnly; } }
        public void Add(TKey key, TValue value)
        {
            _bd.Add(key, value);
        }

        public bool ContainsKey(TKey key)
        {
            return _bd.ContainsKey(key);
        }

        public bool Remove(TKey key)
        {
            return _bd.Remove(key);
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            return _bd.TryGetValue(key, out value);
        }

        public TValue this[TKey key]
        {
            get { return _bd[key]; }
            set { _bd[key] = value; }
        }

        public ICollection<TKey> Keys { get { return _bd.Keys; } }
        public ICollection<TValue> Values { get { return _bd.Values; } }
        
        public void Dispose()
        {
            RxnDecorator.Dispose();
        }
    }
}
