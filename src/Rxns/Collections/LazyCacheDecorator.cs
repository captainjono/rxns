using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading.Tasks;
using Rxns.Health;
using Rxns.Interfaces.Reliability;
using Rxns.Logging;

namespace Rxns.Collections
{
    public interface TRxnStore<T>
    {
        IObservable<T> Get();
        void Clear();
    }

    public interface IKeyValue
    {
        string Key { get; }
        string Value { get; }
    }

    /// <summary>
    /// This decorator is used to lazily persist information in the dictionary to a store. 
    /// Each operation is performed in memory and then queued to be written to the database in an async reliable fashion.
    /// </summary>
    /// <typeparam name="TValue">The key for the dictionary is always a string, this is the type of the value of the dictionary</typeparam>
    public abstract class LazyCacheDecorator<TValue, TStore, TStoreRecord> : ReportsStatus, IDictionary<string, TValue>, IReportHealth 
        where TStore : TRxnStore<TStoreRecord> 
        where TStoreRecord : IKeyValue
    {
        public override string ReporterName
        {
            get { return String.Format("LazyCache<{0}>", _storeTableName); }
        }
        public ISubject<IHealthEvent> Pulse { get; private set; }


        private class DelayedWork
        {
            public string OperationId;
            public string Key;
            public Action<TStore> Work;
        }
        /// <summary>
        /// This is a collection of data store to shard maps. the disposable objects are the worker queues that service the stores
        /// </summary>
        private readonly Dictionary<string, KeyValuePair<TStore, IDisposable>> _stores = new Dictionary<string, KeyValuePair<TStore, IDisposable>>();
        private readonly IDictionary<string, TValue> _decorated;
        private readonly Func<string, TStore> _shardStoreFactory;
        private readonly string _storeTableName;
        private string[] _shards;
        private readonly Func<string, IEnumerable<string>, string> _getShard;
        private readonly IReliabilityManager _reliably;
        private readonly string _partition;
        private readonly Subject<DelayedWork> _delayedQueue = new Subject<DelayedWork>();

        public ICollection<string> Keys { get { return _decorated.Keys; } }
        public ICollection<TValue> Values { get { return _decorated.Values; } }


        /// <summary>
        /// Constructs the decorator
        /// </summary>
        /// <param name="decorated">The dictionary that is wrapped by this class. All operations are completed against this dictionary, and then lazily persisted to the store as a "cached" version</param>
        /// <param name="storeTableName">The name of the table used for each shard to store the data written to the dictionary</param>
        /// <param name="shards">The list of shards that each Key,Value will map to</param>
        /// <param name="shardStoreFactory">Given a shard, return a store that the shard maps to</param>
        /// <param name="getShard">Given a key and the list of shards, return a shard that the key maps to</param>
        /// <param name="reliably">The reliablityManager used to execute the store operations</param>
        /// <param name="partition">The partition in the store table to use. by default, their will be a "" partition</param>
        /// <param name="delayedBurstTimeout">When an Add or Remove operation is queued for each shard in quick succession, only the last operation will be performed as the assumption is that this will superseed the previous. This timeout determins how long the system will wait before doing the last operations. The default is 5seconds.</param>
        /// <param name="delayedBurstScheduler">The scheduler used to time the burst feature. schedulers.default is used if null</param>
        /// <param name="storeScheduler">The scheduler used to performed the delayed work for each shard. The default scheduler is a single threaded EventLoopScheduuler</param>
        public LazyCacheDecorator(IDictionary<string, TValue> decorated, string storeTableName, IObservable<string[]> shards, Func<string, TStore> shardStoreFactory, Func<string, IEnumerable<string>, string> getShard, IReliabilityManager reliably, string partition = "", TimeSpan? delayedBurstTimeout = null, IScheduler delayedBuffer = null, IScheduler storeWorker = null)
        {
            _decorated = decorated;
            _shardStoreFactory = shardStoreFactory;
            _storeTableName = storeTableName;
            _getShard = getShard;
            _reliably = reliably;
            _partition = partition;

            Pulse = new Subject<IHealthEvent>();

            Ensure.NotNull(_partition, "The partition doesnt support null values at the moment");

            //store worker configuration
            delayedBurstTimeout = delayedBurstTimeout ?? TimeSpan.FromSeconds(5);
            delayedBuffer = delayedBuffer ?? Scheduler.Default;
            storeWorker = storeWorker ?? new EventLoopScheduler();
            _shards = new string[] { };

            //cleanup all resources on dispose to flush any pending operations
            new DisposableAction(() => _stores.ForEach(state => state.Value.Value.Dispose())).DisposedBy(this);

            //create a worker for each shard, beause it buffers operations
            //based on keys which can be the same across shards.
            shards.Subscribe(shds =>
            {
                var removed = _shards.Except(shds);
                var added = shds.Except(_shards);

                StartDelayedWorkerFor(added, (TimeSpan)delayedBurstTimeout, delayedBuffer, storeWorker);
                StopDelayedWorkerFor(removed);

                _shards = shds;
                LoadFromStore(shds);
            })
            .DisposedBy(this);

            //initialise everything else & load from cache
            StartDelayedWorkerAllShards(storeWorker);
        }

        public void Shock()
        {
        }

        private void StartDelayedWorkerFor(IEnumerable<string> shards, TimeSpan bufferTimeout, IScheduler delayed, IScheduler worker)
        {
            shards.ForEach(shard => StartDelayedWorkerFor(shard, bufferTimeout, delayed, worker));
        }

        private void StopDelayedWorkerFor(IEnumerable<string> shards)
        {
            shards.ForEach(shard => StopDelayedWorkerFor(shard));
        }

        public abstract IEnumerable<TStoreRecord> GetStoreFromShards(string[] shards);

        private void LoadFromStore(string[] shards)
        {
            GetStoreFromShards(shards)
                .Where(v => v != null)
                .ForEach(keyValue =>
                {
                    //_decorated.Add(StripKey(keyValue.Key), keyValue.Value);
                });
        }

        private string StripKey(string p)
        {
            var keyAndPartition = p.Split('_');
            return keyAndPartition.Take(keyAndPartition.Length - 1).ToStringEach("_");
        }

        /// <summary>
        /// Starts a new delayed worker
        /// </summary>
        /// <param name="shard"></param>
        /// <param name="bufferTimeout"></param>
        /// <param name="delayed"></param>
        /// <param name="workerStore"></param>
        public void StartDelayedWorkerFor(string shard, TimeSpan bufferTimeout, IScheduler delayed, IScheduler workerStore)
        {
            var workBuffer =
            _delayedQueue
                .Where(w => shard == _getShard(w.Key, _shards))
                .BufferFirstLastDistinct(w => "{0}%{1}".FormatWith(GetKey(w.Key), w.OperationId), bufferTimeout, notifyFirst: false, ignoreScheduler: delayed);//cator for "bursts" of information, only logging the last update as its the "latest"

            var worker =
                workBuffer
                    .Monitor(operation => this.TryCatch(() => _reliably.CallDatabase(() => operation.Work(_stores[_getShard(operation.Key, _shards)].Key), workerStore).Wait()), HealthMonitor.ForQueue<DelayedWork>(this, "shard({0})".FormatWith(shard)))
                    .Until();

            _stores.Add(shard, new KeyValuePair<TStore, IDisposable>(_shardStoreFactory(shard), new CompositeDisposable(new DisposableAction(() => workBuffer.FlushBuffer()), worker))); //must flush the buffer before destroying the worker
        }

        public void StopDelayedWorkerFor(string shard)
        {
            if (!_stores.ContainsKey(shard)) return;
            _stores[shard].Value.Dispose();
            _stores.Remove(shard);
        }

        public void StartDelayedWorkerAllShards(IScheduler worker)
        {
            _delayedQueue
                .Where(w => w.Key.IsNullOrWhitespace())
                .Monitor(operation => this.TryCatch(() => _stores.Values.ForEach(store => _reliably.CallDatabase(() => operation.Work(store.Key), worker))), HealthMonitor.ForQueue<DelayedWork>(this, "allShards"))
                .Until()
                .DisposedBy(this);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="immediate"></param>
        /// <param name="delayed"></param>
        /// <param name="shard"></param>
        /// <param name="operationId">If multiple key_operationIds are seen in quick bursts, only the last one will be performed</param>
        public void Do(Action<IDictionary<string, TValue>> immediate, Action<TStore> delayed, string shard = null, string operationId = null)
        {
            immediate(_decorated);
            CurrentThreadScheduler.Instance.Run(() => _delayedQueue.OnNext(new DelayedWork() { Key = shard, Work = delayed, OperationId = operationId }));
        }

        public TReturn Do<TReturn>(Func<IDictionary<string, TValue>, TReturn> immediate, Action<TStore> delayed, string key = null, string operationId = null)
        {
            var result = immediate(_decorated);
            CurrentThreadScheduler.Instance.Run(() => _delayedQueue.OnNext(new DelayedWork() { Key = key, Work = delayed, OperationId = operationId }));

            return result;
        }


        public IEnumerator<KeyValuePair<string, TValue>> GetEnumerator()
        {
            return _decorated.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return _decorated.GetEnumerator();
        }

        public void Add(KeyValuePair<string, TValue> item)
        {
            Add(item.Key, item.Value);
        }

        public abstract void ClearStore();

        public void Clear()
        {
            Do(mem => mem.Clear(),
               s => s.Clear()
            );
        }

        public bool Contains(KeyValuePair<string, TValue> item)
        {
            return _decorated.Contains(item);
        }

        public void CopyTo(KeyValuePair<string, TValue>[] array, int arrayIndex)
        {
            _decorated.CopyTo(array, arrayIndex);
        }

        public bool Remove(KeyValuePair<string, TValue> item)
        {
            return Remove(item.Key);
        }

        public int Count { get { return _decorated.Count; } }
        public bool IsReadOnly { get { return _decorated.IsReadOnly; } }
        public void Add(string key, TValue value)
        {
            Do(d => d.Add(key, value),
                db =>
                {
                    var k = key;
                    var v = value;
                    //db.Run(r =>
                    //{
                    //    var vv = v;
                    //    var kk = k;
                    //    r.Insert(_storeTableName, Deconstruct(key, value, _partition));
                    //});
                },
                key,
                "I"
            );
        }

        //private KeyValueRecord<TValue> Deconstruct(string key, TValue item, string partition = null)
        //{
        //    return new KeyValueRecord<TValue>()
        //    {
        //        Value = item,
        //        Key = GetKey(key),
        //        PartitionKey = partition
        //    };
        //}

        public string GetKey(string key)
        {
            return "{0}_{1}".FormatWith(key, _partition);
        }

        public bool ContainsKey(string key)
        {
            return _decorated.ContainsKey(key);
        }

        public bool Remove(string key)
        {
            return Do(d => d.Remove(key),
                       db =>  db.Get(), //db.Run(r => r.Delete<KeyValueRecord<TValue>>(w => w.Where(v => v.Key == GetKey(key) && v.PartitionKey == _partition), _storeTableName)),
                       key,
                       "U"
                );
        }

        public bool TryGetValue(string key, out TValue value)
        {
            return _decorated.TryGetValue(key, out value);
        }

        public TValue this[string key]
        {
            get
            {
                TValue value;
                TryGetValue(key, out value);

                if (value == null) throw new KeyNotFoundException(key);
                return value;
            }
            set
            {
                Do(d => d[key] = value,
                   db => db.Get(),//(r => r.Update(Deconstruct(key, value, _partition), w => w.Key == GetKey(key) && w.PartitionKey == _partition, _storeTableName)),
                   key,
                   "U"
                );
            }
        }

    }
}
