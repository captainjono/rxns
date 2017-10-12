using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Rxns.Commanding;
using Rxns.Interfaces;
using Rxns.System.Collections.Generic;

namespace Rxns
{
    /// <summary>
    /// An observable sequence which understands filtering and paging
    /// If the query, filtering or paging operations fail, 
    /// and the error is consumed in order to preseve the sequence.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class ObservableQuery<T, TFilter> : IObservableQuery<T, TFilter>
    {
        public ISubject<TFilter> Filter { get; set; }
        public ISubject<ContinuationToken> Continuation { get; set; }

        private readonly Func<TFilter, ContinuationToken, IObservable<T>> _queryFunc;
        private readonly IScheduler _scheduler;
        private readonly CompositeDisposable _resources = new CompositeDisposable();
        private List<IObservable<T>> _syncList = new List<IObservable<T>>();

        private readonly ISubject<Exception> _errors = new Subject<Exception>();
        public IObservable<Exception> Errors { get { return _errors; } }

        public ObservableQuery(Func<TFilter, ContinuationToken, IObservable<T>> queryFunc, ISubject<TFilter> filterStream, ContinuationToken token = null, IScheduler scheduler = null)
        {
            _queryFunc = queryFunc;
            _scheduler = scheduler ?? Scheduler.Default;
            Filter = filterStream;
            Continuation = new BehaviorSubject<ContinuationToken>(token ?? new ContinuationToken()).DisposedBy(this);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="observer"></param>
        /// <returns>A dispoable which flushes all resources associated with the query</returns>
        public IDisposable Subscribe(IObserver<T> observer)
        {
            return Observable.Create<T>(o =>
            {
                _syncList.ForEach(s => s.Subscribe().DisposedBy(_resources));
                                
                _scheduler.Run(() =>
                {
                    GetQuery()
                        .Where(r => r != null)
                        .Subscribe(o.OnNext, e => _errors.OnNext(e))
                        .DisposedBy(_resources);
                });


                return _resources;

            }).Subscribe(observer);
        }

        private IObservable<T> GetQuery()
        {
            return Filter.CombineLatest(Continuation, (f, c) => new { Filter = f, Continuation = c })
                .Buffer(TimeSpan.FromSeconds(1), _scheduler)
                .Where(c => c.Count > 0)
                .Select(s => s.Last())
                .Select(@params =>
                {
                    try
                    {
                        return _queryFunc(@params.Filter, @params.Continuation);
                    }
                    catch (Exception e)
                    {
                        _errors.OnNext(e);
                        return default(T).ToObservable();
                    }
                })
                .Switch()
                .Catch<T, Exception>(e => {
                    _errors.OnNext(e);
                    return GetQuery();
                });
        }

        /// <summary>
        /// Syncs the filter and continuation operations which cause the query to reevaluate,
        /// such that both querys will update at the same time.
        /// 
        /// Subscribing to the result will cause a subscription to both to occour. Disposing the 
        /// subscription will dispose both subscriptions simulatiously.
        /// </summary>
        /// <param name="another"></param>
        /// <returns></returns>
        public IObservableQuery<T, TFilter> SyncWith(IObservableQuery<T, TFilter> another)
        {
            _syncList.Add(another.FilterWith(Filter).LimitWith(Continuation).Publish().RefCount());

            return this;
        }

        /// <summary>
        /// The same as disposing of the subscription. Releases all resources
        /// </summary>
        public void Dispose()
        {
            _resources.DisposeAll();
            _resources.Clear();
        }

        public void OnDispose(IDisposable obj)
        {
            _resources.Add(obj);
        }
    }

    public class FutureSubject<T> : IObservable<T>, IDisposable where T : class
    {
        private BehaviorSubject<T> _values;
        private T _future;

        public FutureSubject(T initalValue)
        {
            _values = new BehaviorSubject<T>(initalValue);
        }

        public void Next()
        {
            if (_future == null)
            {
                Debug.WriteLine("@@@@@ NO MORE RECORDS");
                return;
            }

            _values.OnNext(_future);
            _future = null;
        }

        public void Future(T value)
        {
            _future = value;
        }

        public IDisposable Subscribe(IObserver<T> observer)
        {
            return _values.Subscribe(observer);
        }

        public void Dispose()
        {
            //_values.OnCompleted();
            _values.Dispose();
        }
    }
}
