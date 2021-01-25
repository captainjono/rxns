using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;


namespace Rxns.Collections
{
    public class UseConcurrentReliableOpsWhenCastToIDictionary<T, TV> : IDictionary<T, TV>
    {
        private readonly ConcurrentDictionary<T, TV> _decorated;

        public UseConcurrentReliableOpsWhenCastToIDictionary(ConcurrentDictionary<T, TV> decorated)
        {
            _decorated = decorated;
        }

        public IEnumerator<KeyValuePair<T, TV>> GetEnumerator()
        {
            return _decorated.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public void Add(KeyValuePair<T, TV> item)
        {
            Add(item.Key, item.Value);
        }

        public void Clear()
        {
            _decorated.Clear();
        }

        public bool Contains(KeyValuePair<T, TV> item)
        {
            return _decorated.ContainsKey(item.Key) ? _decorated[item.Key].Equals(item.Value) : false;
        }

        public void CopyTo(KeyValuePair<T, TV>[] array, int arrayIndex)
        {
            throw new NotImplementedException();
        }

        public bool Remove(KeyValuePair<T, TV> item)
        {
            return Remove(item.Key);
        }

        public int Count { get { return _decorated.Count; } }
        public bool IsReadOnly { get { return false; } }
        public bool ContainsKey(T key)
        {
            return _decorated.ContainsKey(key);
        }

        public void Add(T key, TV value)
        {
            _decorated.AddOrUpdate(key, value, (k, old) => value);
        }

        public bool Remove(T key)
        {
            return _decorated.RemoveIfExists(key);
        }

        public bool TryGetValue(T key, out TV value)
        {
            return _decorated.TryGetValue(key, out value);
        }

        public TV this[T key]
        {
            get { return _decorated[key]; }
            set { _decorated[key] = value; }
        }

        public ICollection<T> Keys { get { return _decorated.Keys; } }
        public ICollection<TV> Values { get { return _decorated.Values; } }
    }
}
