using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using StackExchange.Redis;

namespace Rxns.Redis
{
    public interface IRedisBatchDictionary<TKey, TValue> : IDictionary<TKey, TValue>, ICollection<KeyValuePair<TKey, TValue>>
    {
        IDictionary<TKey, TValue> GetValues(TKey[] keys);
    }

    public class RedisDictionary<TKey, TValue> : IDictionary<TKey, TValue>, ICollection<KeyValuePair<TKey, TValue>>, IRedisBatchDictionary<TKey, TValue>
    {
        private const string RedisKeyTemplate = "Dictionary:{0}";

        private static Exception KeyNotFoundException = new KeyNotFoundException("The given key was not present in the dictionary.");
        private static Exception KeyNullException = new ArgumentNullException("key", "Value cannot be null");

        private readonly IDatabase _database;
        private readonly string _redisKey;
        private readonly object _singleThread = new object();

        public string RedisKey
        {
            get { return _redisKey; }
        }

        public RedisDictionary(IDatabase database, string name)
        {
            if (database == null)
            {
                throw new ArgumentNullException("database");
            }
            if (name == null)
            {
                throw new ArgumentNullException("name");
            }

            _database = database;
            _redisKey = string.Format(RedisKeyTemplate, name);
        }

        /// <summary>
        /// NOTE:
        /// This method is AddOrReplace, so it will override any existing keys
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        public void Add(TKey key, TValue value)
        {
            lock (_singleThread)
            {
                Set(key, value);
            }
        }
        
        public bool TryAdd(TKey key, TValue value)
        {
            return Set(key, value);
        }

        public bool ContainsKey(TKey key)
        {
            if (IsKeyNull(key))
            {
                throw KeyNullException;
            }

            return _database.HashExists(_redisKey, key.ToRedisValue());
        }

        public ICollection<TKey> Keys
        {
            get
            {
                return _database.HashKeys(_redisKey).Select(key => key.To<TKey>()).ToList();
            }
        }

        public bool Remove(TKey key)
        {
            lock (_singleThread)
            {
                if (IsKeyNull(key))
                {
                    throw KeyNullException;
                }

                return _database.HashDelete(_redisKey, key.ToRedisValue());
            }
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            lock (_singleThread)
            {
                if (IsKeyNull(key))
                {
                    throw KeyNullException;
                }

                value = default(TValue);
                var redisValue = _database.HashGet(_redisKey, key.ToRedisValue());
                if (redisValue.IsNullOrEmpty)
                {
                    return false;
                }
                value = (TValue) redisValue.;

                return true;
            }
        }

        public ICollection<TValue> Values
        {
            get
            {
                return _database.HashValues(_redisKey).Select(val => (TValue) val).ToList();
            }
        }

        public TValue this[TKey key]
        {
            get
            {
                TValue value;
                if (!TryGetValue(key, out value))
                {
                    throw KeyNotFoundException;
                }
                return value;
            }
            set
            {
                Add(key, value);
            }
        }

        void ICollection<KeyValuePair<TKey, TValue>>.Add(KeyValuePair<TKey, TValue> item)
        {
            Add(item.Key, item.Value);
        }

        public void Clear()
        {
            lock (_singleThread)
            {
                _database.KeyDelete(_redisKey);
            }
        }

        bool ICollection<KeyValuePair<TKey, TValue>>.Contains(KeyValuePair<TKey, TValue> item)
        {
            TValue value;
            bool keyExists = TryGetValue(item.Key, out value);
            return keyExists && object.Equals(item.Value, value);
        }

        void ICollection<KeyValuePair<TKey, TValue>>.CopyTo(KeyValuePair<TKey, TValue>[] array, int index)
        {
            if (array == null)
            {
                throw new ArgumentNullException("array");
            }
            if (index < 0 || index > array.Length)
            {
                throw new ArgumentOutOfRangeException("index");
            }
            if (array.Length - index < this.Count)
            {
                throw new ArgumentException("Destination array is not long enough to copy all the items in the collection. Check array index and length.");
            }

            foreach (var item in this)
            {
                array[index++] = item;
            }
        }

        public int Count
        {
            get
            {
                long count = _database.HashLength(_redisKey);
                if (count > int.MaxValue)
                {
                    throw new OverflowException("Count exceeds maximum value of integer.");
                }
                return (int)count;
            }
        }

        bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly
        {
            get
            {
                return false;
            }
        }

        bool ICollection<KeyValuePair<TKey, TValue>>.Remove(KeyValuePair<TKey, TValue> item)
        {
            return Remove(item.Key);
        }

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
        {
            return _database
                        .HashScan(_redisKey)
                        .Select(he => new KeyValuePair<TKey, TValue>((TKey) he.Name.Box(), (TValue) he.Value.Box()))
                        .GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        private bool Set(TKey key, TValue value)
        {
            return _database.HashSet(_redisKey, key.ToRedisValue(), value.ToRedisValue());
        }

        private bool IsKeyNull(TKey key)
        {
            return !typeof(TKey).IsValueType && key == null;
        }

        public IDictionary<TKey, TValue> GetValues(TKey[] keys)
        {
            var hashEntries = _database.HashGetAll(_redisKey).Where(r => keys.Select(k => k.ToRedisValue()).ToList().Contains(r.Name.ToString())).ToList();
            return hashEntries.ToDictionary(e => (TKey) e.Name, e => e.Value.To<TValue>());
        }
    }
}