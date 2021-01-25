using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.PlatformServices;
using System.Threading;


namespace Rxns.Collections
{
    /// <summary>
    /// A set of factory functions to create caches in standard ways
    /// </summary>
    public static class ExpiringCache
    {
        /// <summary>
        /// Creates a IDicitonary based cache with
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="cache"></param>
        /// <param name="expireyTime"></param>
        /// <param name="scheduler"></param>
        /// <returns></returns>
        public static ExpiringCacheDecorator<IDictionary<TKey, TValue>, TKey, TValue> CreateWith<TKey, TValue>(IDictionary<TKey, TValue> cache, TimeSpan? expireyTime = null, IScheduler scheduler = null)
        {
            return new ExpiringCacheDecorator<IDictionary<TKey, TValue>, TKey, TValue>(cache,
                            (dict, id) => dict[id],
                            (dict, id, value) => dict.Add(id, value),
                            (dict, id) => dict.ContainsKey(id),
                            (dict, id) => dict.Remove(id),
                            expireyTime ?? TimeSpan.FromMinutes(20),
                            TimeSpan.FromMinutes(10),
                            scheduler ?? Scheduler.Default);
        }

        /// <summary>
        /// Creates a IDictionaryCache that is thread-safe
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="expireyTime"></param>
        /// <param name="scheduler"></param>
        /// <returns></returns>
        public static ExpiringCacheDecorator<IDictionary<TKey, TValue>, TKey, TValue> CreateConcurrent<TKey, TValue>(TimeSpan? expireyTime = null, IScheduler scheduler = null)
        {
            var cache = new UseConcurrentReliableOpsWhenCastToIDictionary<TKey, TValue>(new ConcurrentDictionary<TKey, TValue>());
            return CreateWith(cache, expireyTime, scheduler);
        }
    }

    /// <summary>
    /// A cache decorator which expires keys after a non-sliding timespan.
    /// The GetOrLookup operations are thread-safe with the cleaning of the cache
    /// on a per-key basis
    /// </summary>
    /// <typeparam name="TObj"></typeparam>
    /// <typeparam name="TKey"></typeparam>
    /// <typeparam name="TValue"></typeparam>
    public class ExpiringCacheDecorator<TObj, TKey, TValue> : IExpiringCache<TKey, TValue>
    {
        private class ExpireyLock
        {
            public DateTime Started { get; set; }
            public SemaphoreSlim Lock { get; set; }

            public ExpireyLock()
            {
                Started = SystemClock.Now();
                Lock = new SemaphoreSlim(0, 1);

                Lock.Release(1);
            }
        }

        private readonly TObj _decorated;
        private readonly Action<TObj, TKey, TValue> _setter;
        private readonly Func<TObj, TKey, TValue> _getter;
        private readonly Func<TObj, TKey, bool> _exists;
        private readonly Action<TObj, TKey> _removeFunc;
        private readonly TimeSpan _expiration;
        private readonly TimeSpan _lockTime;
        private readonly ConcurrentDictionary<TKey, ExpireyLock> _expiryTimes = new ConcurrentDictionary<TKey, ExpireyLock>();
        private readonly IScheduler _cleanupSchedulder;
        private IDisposable _cleanerWaiter;

        /// <summary>
        /// todo: modify class such that "expiring" function is provided, that is pre-configured to "expire things". The user adds something to it to expire, and gives a function that expires it
        /// implement timeperiod exipirer, rxn expirer which already has a ref to the eventmanager, expires things given certaine events occour.
        /// or
        /// at the same time stream the rxn to the aggregate on an "input" stream to augment its output stream. a Isubject? it can search for methods on the agg that take the rxn (exactly?) and then rxn them.
        /// 
        /// the cahce could sit in between? it doesnt need to goto the store? Or is the cache best for lookup matching - circut breaker on cache match?
        ///     
        /// </summary>
        /// <param name="decorated"></param>
        /// <param name="getter"></param>
        /// <param name="setter"></param>
        /// <param name="exists"></param>
        /// <param name="removeFunc"></param>
        /// <param name="expiration"></param>
        /// <param name="lockTime"></param>
        /// <param name="cleanupSchedulder"></param>
        public ExpiringCacheDecorator(TObj decorated, Func<TObj, TKey, TValue> getter, Action<TObj, TKey, TValue> setter,
            Func<TObj, TKey, bool> exists, Action<TObj, TKey> removeFunc, TimeSpan expiration, TimeSpan? lockTime = null,
            IScheduler cleanupSchedulder = null)
        {
            _getter = getter;
            _setter = setter;
            _decorated = decorated;
            _exists = exists;
            _removeFunc = removeFunc;
            _expiration = expiration;
            _cleanupSchedulder = cleanupSchedulder ?? Scheduler.Default;
            _lockTime = lockTime ?? TimeSpan.FromMinutes(10);
        }

        public IObservable<TValue> GetOrLookup(TKey key, Func<TKey, TValue> lookupFunc)
        {
            var keyLock = _expiryTimes.GetOrAdd(key, new ExpireyLock());

            return Rxn.DfrCreate(() =>
            {
                    //keyLock.Lock.Wait(_lockTime);

                    if (!_exists(_decorated, key))
                {
                    var newValue = lookupFunc(key);
                    Set(key, newValue);

                    return newValue;
                }

                return _getter(_decorated, key);
            });
            // .FinallyR(() => keyLock.Lock.Release());
        }

        public IObservable<TValue> GetOrLookup(TKey key, Func<TKey, IObservable<TValue>> lookupFunc)
        {
            var keyLock = _expiryTimes.GetOrAdd(key, new ExpireyLock());

            //we need to defer create so people who wont subscribe wont deadlock the cache.
            //because they will take a lock but never release it because finally isnt called
            return Rxn.DfrCreate<TValue>(o =>
            {
                    // keyLock.Lock.Wait();

                    if (_exists(_decorated, key))
                {
                    o.OnNext(_getter(_decorated, key));
                    o.OnCompleted();

                    return Disposable.Empty;
                }

                return lookupFunc(key).Subscribe(newValue =>
                {
                    try
                    {
                        Set(key, newValue);
                        o.OnNext(newValue);
                    }
                    catch (Exception e)
                    {
                        o.OnError(e);
                    }
                    finally
                    {
                        o.OnCompleted();
                    }
                }, o.OnError);
            });
            //.FinallyR(() => keyLock.Lock.Release());
        }

        /// <summary>
        /// Not thread safe
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public TValue Get(TKey key)
        {
            return _getter(_decorated, key);
        }

        /// <summary>
        /// Not thread safe
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        public void Set(TKey key, TValue value)
        {
            _setter(_decorated, key, value);

            _expiryTimes.GetOrAdd(key, new ExpireyLock());
            StartCacheCleaner();
        }

        public void Remove(TKey key)
        {
            _removeFunc(_decorated, key);
        }

        public bool Contains(TKey key)
        {
            return _exists(_decorated, key);
        }

        private void StartCacheCleaner()
        {
            //we need to defer create so people who wont subscribe wont deadlock the cache.
            //because they will take a lock but never release it because finally isnt called
            if (!_expiryTimes.AnyItems()) return;
            if (_cleanerWaiter != null) return;

            var nextExpiration = _expiryTimes.Values.Min(m => m.Started);

            _cleanerWaiter = Observable.Timer(SystemClock.Now() - nextExpiration, _cleanupSchedulder)
            .Do(_ =>
            {
                _cleanerWaiter = null;

                CleanCache(SystemClock.Now());
                StartCacheCleaner();
            })
            .Catch<long, Exception>(error =>
            {
                Debug.WriteLine("Swallowing {0}", error);
                return Observable.Return<long>(0);
            })
            .Subscribe();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="withExpireyBefore"></param>
        public void CleanCache(DateTime withExpireyBefore)
        {
            ExpireyLock dontcare;

            _expiryTimes
                .Where(w => w.Value.Started + _expiration < withExpireyBefore)
                .ToArray()
                .ForEach(record =>
                {
                    try
                    {
                            //resource.Lock.Wait();
                            while (_expiryTimes.TryRemove(record.Key, out dontcare))
                        {
                            _removeFunc(_decorated, record.Key);
                            if (!_expiryTimes.ContainsKey(record.Key)) break;
                        }
                    }
                    finally
                    {
                            //resource.Lock.Release();
                        }
                });
        }
    }


}
