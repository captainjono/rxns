using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns.Cloud
{
    /// <summary>
    /// This queue does work for each shard on a different thread in an effort to create a higher degree of in-app tenanted seperation
    /// and reduce comptetion for resources that a hungry tenant may demand.
    /// </summary>
    /// <typeparam name="TQueueItem"></typeparam>
    public abstract class ShardingQueueProcessingService<TQueueItem> : ReportStatusService, IRxnPublisher<IRxn>
    {
        public string QueueName { get; set; }
        public int QueueCurrent = 0;
        public int QueueSize { get; set; }
        protected Action<TQueueItem> _queueFunc;
        protected IScheduler _queueWorkerScheduler;
        protected readonly List<IDisposable> _queueResources = new List<IDisposable>();
        protected ISubject<TQueueItem> _shardQueue;
        /// <summary>
        /// an thread safe int that is used to represent a bool
        /// </summary>
        protected int _started;
        protected readonly bool _isSynchronous;
        private int _workerId;
        protected Action<IRxn> _publish { get; private set; }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="queueName">The name of the queue</param>
        /// <param name="isSynchronous">If set to true, will ensure that if something is awaited in the ProcessingQueue function, the next queue item wont be dequeued until ProcessQueue returns. If not, next item will be dequeued straight away</param>
        /// <param name="queueWorkerScheduler">Where the queue work is scheduled, by default its the TaskPool</param>

        public ShardingQueueProcessingService(string queueName = null, bool isSynchronous = true, IScheduler queueWorkerScheduler = null)
        {
            QueueName = queueName ?? GetType().Name;
            _queueWorkerScheduler = queueWorkerScheduler ?? NewThreadScheduler.Default;
            _isSynchronous = isSynchronous;

            //cleanup resources
            OnDispose(new DisposableAction(StopQueue));
        }

        public void ConfigiurePublishFunc(Action<IRxn> publish)
        {
            _publish = publish;
            publish(new AppStatusInfoProviderEvent()
            {
                Info = () => new []
                {
                    new AppStatusInfo("Queue", QueueName),
                    new AppStatusInfo("QueueC", QueueCurrent),
                    new AppStatusInfo("QueueSize", QueueSize)
                }
            });
        }

        protected IDisposable CreateOneShardForEach<T>(IObservable<T[]> itemInList, Func<T, TQueueItem, bool> shardSelector)
        {
            return itemInList.Do(_ =>
            {
                lock (itemInList)
                {
                    _queueResources.DisposeAll();
                    _queueResources.Clear();
                }
            })
            .Select(tenants =>
            {
                return tenants.Select(t =>
                {
                    var tt = t;
                    return new Func<TQueueItem, bool>(a => shardSelector(tt, a));
                })
                .ToArray();
            })
            .Do(tenants =>
            {
                _queueWorkerScheduler = TaskPoolSchedulerWithLimiter.ToScheduler(tenants.Length > 0 ? tenants.Length : 8);

                lock (itemInList)
                    StartQueue(tenants);
            })
            .Until(OnError);
        }

        /// <summary>
        /// todo: remove shared state, return IDisposable from this to stop the queue
        /// </summary>
        /// <param name="workerShardSelector"></param>
        protected virtual void StartQueue(params Func<TQueueItem, bool>[] workerShardSelector)
        {
            foreach (var where in workerShardSelector)
            {
               StartQueue(where);
            }
        }

        public void StartQueue(Func<TQueueItem, bool> where)
        {
            try
            {
                if (Interlocked.CompareExchange(ref _started, 1, 0) != 1)
                {
                    QueueSize = 8;
                    _shardQueue = new Subject<TQueueItem>();
                    _queueFunc = item => _shardQueue.OnNext(item);
                    _workerId = 0;

                    OnInformation("Queue configured as {0}", _isSynchronous ? "sync" : "async");
                }

                var consumer = _shardQueue.Where(where);
                //need this incase the implementor "awaits", which causes the consuming enumerable to advance before the previous consumer has fully processed "not a proper queue"
                if (_isSynchronous) consumer = consumer.Synchronize(where);

                consumer
                    .ObserveOn(_queueWorkerScheduler)
                    .Do(_ => Interlocked.Increment(ref QueueCurrent))
                    .SelectMany(request => this.ReportExceptions(() => ProcessQueueItem(request).Where(@event => @event != null).ToArray()))
                    .Do(@event => @event.ForEach(_publish))
                    .Select(_ => new Unit())
                    .Catch<Unit, Exception>(e =>
                    {
                        OnError(e);
                        return new Unit().ToObservable();
                    })
                    .Do(_ => Interlocked.Decrement(ref QueueCurrent))
                    .Subscribe(_ => { },
                        error =>
                        {
                            if (error is OperationCanceledException)
                                OnVerbose("Queue processing has been stopped");
                            else
                            {
                                OnError("Terminating the consumer for this queue processor due to: {0}", error);
                            }
                        })
                    .DisposedBy(_queueResources);

                OnVerbose("[{0}] Started sharding consumer", Interlocked.Increment(ref _workerId));
            }
            catch (Exception e)
            {
                OnError(e);
            }
        }

        public void StopQueue()
        {
            if (Interlocked.CompareExchange(ref _started, 0, 1) == 0) return;

            _queueResources.DisposeAll();
        }

        public void Queue(TQueueItem item)
        {
            OnQueued(item);
            _queueFunc(item);
        }

        public virtual void OnQueued(TQueueItem item)
        {

        }

        protected virtual IObservable<IRxn> ProcessQueueItem(TQueueItem item)
        {
            return Rxn.DfrCreate(() => ProcessQueueItemSync(item));
        }

        protected virtual IRxn ProcessQueueItemSync(TQueueItem item)
        {
            return null;
        }
    }
}
