using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using Rxns;

namespace System.Reactive
{
    /// <summary>
    /// Extensions for the observable namespace
    /// </summary>
    public static class IObservableExtensions
    {
        /// <summary>
        /// Throttles the source sequence to the specified number of items,
        /// resetting the count each time a signal is observed
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="source"></param>
        /// <param name="signal">The signaling sequence that resets the item count</param>
        /// <param name="itemsForEachSignal">The maximum number of items passed through for each signal</param>
        /// <returns></returns>
        public static IObservable<T> Throttle<T>(this IObservable<T> source, IObservable<Unit> signal, int itemsForEachSignal)
        {
            var deferred = new ConcurrentQueue<T>();
            var items = new ReplaySubject<T>();
            var sentItems = 0;

            Func<T, bool> send = fm =>
            {
                if (Interlocked.Increment(ref sentItems) <= (itemsForEachSignal))
                {
                    items.OnNext(fm);
                    return true;
                }
                else
                {
                    deferred.Enqueue(fm);
                    return false;
                }
            };

            var signalSub = signal.Subscribe(_ =>
            {
                try
                {
                    Interlocked.Exchange(ref sentItems, 0);

                    while (true)
                    {
                        T next;
                        if (deferred.TryDequeue(out next))
                        {
                            if (!send(next))
                                break;
                        }
                        else
                        {
                            if (deferred.IsEmpty)
                            {
                                break;
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    items.OnError(e);
                }
            });

            var sourceSub = source.Subscribe(fm =>
            {
                send(fm);
            },
                error =>
                {
                    items.OnError(error);
                    signalSub.Dispose();
                },
                onCompleted: () =>
                {
                    items.OnCompleted();
                    signalSub.Dispose();
                });

            return items;
        }

        public static IObservable<T> DoBuffered<T>(this IObservable<T> source, Action<T> @do, TimeSpan doEvery)
        {
            return RxObservable.DfrCreate<T>(o => source.Subscribe(o))
                               .Throttle(doEvery)
                               .Do(@do);
        }
        
        public static IObservable<T> Throttle<T>(this IObservable<T> source, IObservable<Unit> signal, int initalPool, int itemsForEachSignal)
        {
            if (initalPool < 1) throw new ArgumentException("must be greater then zero", "initalPool");

            var pool = source.Take(initalPool - 1);
            return source.Skip(initalPool - 1).Throttle(signal, itemsForEachSignal).Merge(pool);
        }

        public static IObservable<T> ToObservable<T>(this T obj)
        {
            return Observable.Return(obj);
        }

        public static IObservable<T> ToObservableSequence<T>(this IEnumerable<T> obj)
        {
            return Observable.ToObservable(obj);
        }

        public static T WaitR<T>(this IObservable<T> obj)
        {
            return obj == null ? default(T) : obj.ToArray().Wait().FirstOrDefault();
        }


        public static IObservable<T> ToObservable<T>(this Func<T> action)
        {
            return Observable.Create<T>(o =>
            {
                try
                {
                    var result = action();
                    o.OnNext(result);
                    o.OnCompleted();
                }
                catch (Exception e)
                {
                    o.OnError(e);
                }

                return Disposable.Empty;
            });
        }

        public static IObservable<TResult> ContinueWith<TSource, TResult>(this IObservable<TSource> source, Func<IObservable<TResult>> continuation)
        {
            return source.IsEmpty().SelectMany(Observable.Defer(continuation));
        }

     

        /// <summary>
        /// Returns an observable sequence of the value of a property when <paramref name="source"/> raises <seealso cref="INotifyPropertyChanged.PropertyChanged"/> for the given property.
        /// https://github.com/LeeCampbell/RxCookbook/blob/master/Model/PropertyChange.md
        /// </summary>
        /// <typeparam name="T">The type of the source object. Type must implement <seealso cref="INotifyPropertyChanged"/>.</typeparam>
        /// <typeparam name="TProperty">The type of the property that is being observed.</typeparam>
        /// <param name="source">The object to observe property changes on.</param>
        /// <param name="property">An expression that describes which property to observe.</param>
        /// <returns>Returns an observable sequence of the property values as they change.</returns>
        public static IObservable<TProperty> WhenAny<T, TProperty>(this T source,
            params Expression<Func<T, TProperty>>[] properties)
            where T : INotifyPropertyChanged
        {
            return Observable.Create<TProperty>(o =>
            {
                var propertyNames = new Dictionary<string, Func<T, TProperty>>();

                foreach (var property in properties)
                {
                    propertyNames.Add(property.GetPropertyInfo().Name, property.Compile());
                }

                return FromPropertyChanged<PropertyChangedEventHandler, PropertyChangedEventArgs>(
                            handler => handler.Invoke,
                            h => source.PropertyChanged += h,
                            h => source.PropertyChanged -= h)
                            .Where(e => propertyNames.ContainsKey(e.EventArgs.PropertyName))
                            .Select(e => propertyNames[e.EventArgs.PropertyName].Invoke(source))
                            .Subscribe(o);

            });
        }

        public static IObservable<TValue> WhenChanged<T, TValue>(this T source,
            Expression<Func<T, TValue>> propertyExpression)
            where T : INotifyPropertyChanged
        {
            return source.WhenChanged(propertyExpression, false);
        }

        public static IObservable<TValue> WhenChanged<T, TValue>(this T source, Expression<Func<T, TValue>> propertyExpression, bool observeInitialValue, IScheduler scheduler = null)
            where T : INotifyPropertyChanged
        {
            var property = (MemberExpression)propertyExpression.Body;
            var getValue = propertyExpression.Compile();

            var observable = FromPropertyChanged<PropertyChangedEventHandler, PropertyChangedEventArgs>(
                handler => handler.Invoke,
                h => source.PropertyChanged += h,
                h => source.PropertyChanged -= h)
                .ObserveOn(scheduler ?? Scheduler.Default)
                .Where(e => e.EventArgs.PropertyName.Equals(property.Member.Name))
                .Select(e => getValue(source));

            return observeInitialValue ? observable.Merge(Observable.Return(getValue(source))) : observable;
        }

        public static IObservable<EventPattern<TEvent>> FromPropertyChanged<THandle, TEvent>(Func<EventHandler<TEvent>, THandle> conversion, Action<THandle> addHandler, Action<THandle> removeHandler)
        {
            return Observable.Create<EventPattern<TEvent>>(o =>
            {
                EventHandler<TEvent> handl = (sender, @event) =>
                {
                    try
                    {
                        o.OnNext(new EventPattern<TEvent>(sender, @event));
                    }
                    catch (Exception e)
                    {
                        o.OnError(e);
                    }
                };
                var val = conversion(handl);
                addHandler(val);

                return new DisposableAction(() =>
                {
                    removeHandler(val);
                    o.OnCompleted();
                });
            });
        }

        /// <summary>
        /// A reliable version of finally that doesnt matter how the user handles the resulting
        /// obsrevable, it will always be called
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="source"></param>
        /// <param name="finallyAction"></param>
        /// <returns></returns>
        public static IObservable<T> FinallyR<T>(this IObservable<T> source, Action finallyAction)
        {
            return Observable.Create<T>(o =>
            {
                var finallyOnce = Disposable.Create(finallyAction);
                var subscription = source.Subscribe(
                    o.OnNext,
                    ex =>
                    {
                        try
                        {
                            o.OnError(ex);
                        }
                        finally
                        {
                            finallyOnce.Dispose();
                        }
                    },
                    () =>
                    {
                        try
                        {
                            o.OnCompleted();
                        }
                        finally
                        {
                            finallyOnce.Dispose();
                        }
                    });

                return new CompositeDisposable(subscription, finallyOnce);

            });
        }


        /// <summary>
        /// Transforms a chatty sequence into a smooth one by ignoring the values
        /// that occour during bursts of chattyness, only propergating the first
        /// and last values during this time, ignoring the rest. The timer is only activated when values
        /// are produced
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="source"></param>
        /// <param name="ignorePeriod"></param>
        /// <returns></returns>
        public static IObservable<T> BufferFirstLast<T>(this IObservable<T> source, TimeSpan ignorePeriod, bool notifyFirst = true, bool notifyLast = true, IScheduler ignoreScheduler = null)
        {
            return Observable.Defer<T>(() => RxObservable.Create<T>(o =>
            {
                T last = default(T);
                IDisposable timer = null;
                Action startTimer = () => timer = Observable.Timer(ignorePeriod, ignoreScheduler ?? Scheduler.Default)
                                                            .Subscribe(_ =>
                                                            {
                                                                if (notifyLast) o.OnNext(last);
                                                                timer = null;
                                                            });
                var sub = source.Subscribe(t =>
                {
                    last = t;

                    if (timer != null)
                    {
                        timer.Dispose();
                        startTimer();
                        return;
                    }

                    if (notifyFirst) o.OnNext(last);
                    startTimer();
                },
                o.OnError,
                o.OnCompleted);

                return sub;
            }));
        }

        public static IObservable<T> If<T>(this IObservable<T> source, Func<T, bool> when, Action<T> @do)
        {
            return source.Do(t =>
            {
                if (when(t)) @do(t);
            });
        }

        public static IObservable<T> Monitor<T>(this IObservable<T> source, params IMonitorAction<T>[] triggers)
        {
            foreach (var trigger in triggers)
                source = source.If(trigger.When, trigger.Do);

            return source;
        }

        public static IObservable<T> MonitorR<T>(this IObservable<T> source, params IMonitorAction<T>[] triggers)
        {
            foreach (var trigger in triggers)
                source = source.If(trigger.When, trigger.Do);

            return source.FinallyR(() =>
            {
                foreach (var trg in triggers)
                    if (trg.When(default(T))) trg.Do(default(T));
            });
        }

        //public static T Monitor<T>(this Func<T> toMonitor, Action<Exception> onError, params MonitorAction<T>[] triggers)
        //{
        //    try
        //    {
        //        return toMonitor(evnt);
        //    }
        //    catch (Exception e)
        //    {
        //        OnError((processor as IReportStatus ?? reactor), processor, evnt, e);
        //        return Observable.Empty<IRxn>();
        //    }
        //}

        public static IObservable<T> Monitor<T>(this IObservable<T> source, Action<T> action, IMonitorActionFactory<T> backpressureCfg)
        {
            return source.Monitor(backpressureCfg.Before()).Do(action).Monitor(backpressureCfg.After());

        }

        public static IObservable<T> Monitor<T>(this IObservable<T> source, Action<T> action, params IMonitorActionFactory<T>[] backpressureActions)
        {
            return source.Monitor(backpressureActions.Select(b => b.Before()).ToArray()).Do(action).Monitor(backpressureActions.Select(b => b.After()).ToArray());

        }

        public static IObservable<T> Filter<T>(this IObservable<T> source, FilterAction<T> trigger)
        {
            return source.SelectMany(t => trigger.When(t) ? trigger.Do(t) : new[] { t });
        }

        public static BufferedObservable<T> BufferFirstLastDistinct<T>(this IObservable<T> source, Func<T, object> distinctSelector, TimeSpan ignorePeriod, bool notifyFirst = true, bool notifyLast = true, IScheduler ignoreScheduler = null)
        {
            Action doFlush = null;
            Action flushBuffer = () => { if (doFlush != null) doFlush(); }; //omg what a hack but meh, it works!

            return new BufferedObservable<T>(Observable.Defer<T>(() => RxObservable.Create<T>(o =>
            {
                T last = default(T);
                object distinct = null;
                IDisposable timer = null;
                Action<T> lastAction = (t) =>
                {
                    if (timer == null) return;
                    timer = null;
                    if (notifyLast)
                    {
                        o.OnNext(t);
                    }
                };
                var singleThread = new object();

                //set out flush operation now. 
                doFlush = () => lastAction(last);

                Action<T> startBufferTimer = (t) =>
                {

                    timer = Observable.Timer(ignorePeriod, ignoreScheduler ?? Scheduler.Default)
                        .Subscribe(_ =>
                        {
                            lock (singleThread)
                            {
                                lastAction(t);
                            }
                        });
                };
                return source.Subscribe(t =>
                {
                    lock (singleThread)
                    {
                        var latestDistinct = distinctSelector(t);

                        if (timer != null)
                        {
                            if (distinct != null && !distinct.Equals(latestDistinct))
                            {
                                timer.Dispose();
                                lastAction(last);
                            }
                            else
                            {
                                timer.Dispose();
                                startBufferTimer(t);
                                return;
                            }
                        }

                        distinct = latestDistinct;
                        last = t;
                        if (notifyFirst)
                        {
                            o.OnNext(t);
                        }
                        startBufferTimer(t);
                    }
                },
                o.OnError,
                o.OnCompleted);
            })),
            flushBuffer);
        }

        public class BufferedObservable<T> : IObservable<T>
        {
            private readonly IObservable<T> _wrapped;
            public Action FlushBuffer { get; private set; }

            public BufferedObservable(IObservable<T> wrapped, Action flushBuffer)
            {
                _wrapped = wrapped;
                FlushBuffer = flushBuffer;
            }

            public IDisposable Subscribe(IObserver<T> observer)
            {
                return _wrapped.Subscribe(observer);
            }
        }
    }
}