using System;
using System.Diagnostics;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Threading;
using Rxns;
using Rxns.Interfaces.Reliability;

namespace RxnsDemo.AzureB2C.Rxns.Tenant
{
    public class BatchingViewProcessor
    {
        private readonly ITenantDatabaseFactory _pool;
        protected readonly IReliabilityManager _reliably;
        private readonly IScheduler _dbScheduler;

        public BatchingViewProcessor(ITenantDatabaseFactory pool, IReliabilityManager reliably, IScheduler dbScheduler = null)
        {
            _pool = pool;
            _reliably = reliably;
            _dbScheduler = dbScheduler;
            _dbScheduler = dbScheduler ?? RxnSchedulers.Immediate;
        }

        /// <summary>
        /// For times when you want to maintain a single transaction for an entire sequence
        /// of calls to the database. Will retry on transient errors
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="tenant"></param>
        /// <param name="ormTask"></param>
        /// <returns></returns>
        protected virtual T ReliablyRun<T>(string tenant, Func<IOrmBatchContext, T> ormTask)
        {
            var batch = _pool.GetContext(tenant).StartBatch();

            using (var trans = batch.Connection.BeginTransaction())
            {
#if DEBUG
                var timer = new Stopwatch();
                timer.Start();
#endif
                var result = _reliably.CallDatabase(() => ormTask(batch), _dbScheduler).WaitR();
                trans.Commit();
#if DEBUG
                timer.Stop();
                Console.WriteLine("[{0}] Duration => {1}", Thread.CurrentThread.ManagedThreadId, timer.Elapsed);
#endif
                return result;
            }
        }

        /// <summary>
        /// For times when you want to maintain a single transaction for an entire sequence
        /// of calls to the database. Will retry on transient errors
        /// </summary>
        /// <param name="tenant"></param>
        /// <param name="ormTask"></param>
        protected virtual void ReliablyRun(string tenant, Action<IOrmBatchContext> ormTask)
        {
            var batch = _pool.GetContext(tenant).StartBatch();

            using (var trans = batch.Connection.BeginTransaction())
            {
#if DEBUG
                var timer = new Stopwatch();
                timer.Start();
#endif
                _reliably.CallDatabase(() => ormTask(batch), _dbScheduler).WaitR();
                trans.Commit();
#if DEBUG
                timer.Stop();
                Console.WriteLine("[{0}] Duration => {1}", Thread.CurrentThread.ManagedThreadId, timer.Elapsed);
#endif
            }
        }

        /// <summary>
        /// For times when you want performance and are not mutating database records
        /// </summary>
        /// <param name="tenant"></param>
        /// <param name="ormTask"></param>
        protected virtual void ReliablyLookup(string tenant, Action<IOrmContext> ormTask)
        {
            var batch = _pool.GetContext(tenant);

#if DEBUG
            var timer = new Stopwatch();
            timer.Start();
#endif
            _reliably.CallDatabase(() => ormTask(batch), _dbScheduler).WaitR();
#if DEBUG
            timer.Stop();
            Console.WriteLine("[{0}] Duration => {1}", Thread.CurrentThread.ManagedThreadId, timer.Elapsed);
#endif
        }

        /// <summary>
        /// For times when you want performance and are not mutating database records
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="tenant"></param>
        /// <param name="ormTask"></param>
        /// <returns></returns>
        protected virtual T ReliablyLookup<T>(string tenant, Func<IOrmContext, T> ormTask)
        {
            var batch = _pool.GetContext(tenant);

#if DEBUG
            var timer = new Stopwatch();
            timer.Start();
#endif
            var result = _reliably.CallDatabase(() => ormTask(batch), _dbScheduler).WaitR();
#if DEBUG
            timer.Stop();
            Console.WriteLine("[{0}] Duration => {1}", Thread.CurrentThread.ManagedThreadId, timer.Elapsed);
#endif
            return result;
        }
    }
}
