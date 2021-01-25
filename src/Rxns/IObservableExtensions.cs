using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using Rxns;
using Rxns.Collections;
using Rxns.Interfaces;


namespace System.Reactive
{
    /// <summary>
    /// Extensions for the observable namespace
    /// </summary>
    public static partial class IObservableExtensions
    {
        ///// <summary>
        ///// Throttles the source sequence to the specified number of items,
        ///// resetting the count each time a signal is observed
        ///// </summary>
        ///// <typeparam name="T"></typeparam>
        ///// <param name="source"></param>
        ///// <param name="signal">The signaling sequence that resets the item count</param>
        ///// <param name="itemsForEachSignal">The maximum number of items passed through for each signal</param>
        ///// <returns></returns>
        //public static IObservable<T> Throttle<T>(this IObservable<T> source, IObservable<Unit> signal, int itemsForEachSignal)
        //{
        //    var deferred = new Queue<T>();
        //    var items = new ReplaySubject<T>();
        //    var sentItems = 0;

        //    Func<T, bool> send = fm =>
        //    {
        //        if (Interlocked.Increment(ref sentItems) <= (itemsForEachSignal))
        //        {
        //            items.OnNext(fm);
        //            return true;
        //        }
        //        else
        //        {
        //            deferred.Enqueue(fm);
        //            return false;
        //        }
        //    };

        //    var signalSub = signal.Subscribe(_ =>
        //    {
        //        try
        //        {
        //            Interlocked.Exchange(ref sentItems, 0);

        //            while (true)
        //            {
        //                T next;
        //                if (deferred.TryDequeue(out next))
        //                {
        //                    if (!send(next))
        //                        break;
        //                }
        //                else
        //                {
        //                    if (deferred.IsEmpty)
        //                    {
        //                        break;
        //                    }
        //                }
        //            }
        //        }
        //        catch (Exception e)
        //        {
        //            items.OnError(e);
        //        }
        //    });

        //    var sourceSub = source.Subscribe(fm =>
        //    {
        //        send(fm);
        //    },
        //        error =>
        //        {
        //            items.OnError(error);
        //            signalSub.Dispose();
        //        },
        //        onCompleted: () =>
        //        {
        //            items.OnCompleted();
        //            signalSub.Dispose();
        //        });

        //    return items;
        //}

        public static IObservable<T> DoBuffered<T>(this IObservable<T> source, Action<T> @do, TimeSpan doEvery, IScheduler scheduler = null)
        {
            return Rxn.DfrCreate<T>(o => source.Subscribe(o))
                               .Throttle(doEvery, scheduler ?? RxnSchedulers.Default)
                               .Do(@do);
        }

        //public static IObservable<IFileMeta> ThrottleBySize(this IObservable<IFileMeta> source, IObservable<Unit> signal, int bytesForeachSignal)
        //{
        //    return Observable.Create<IFileMeta>(items =>
        //    {

        //        var deferred = new ConcurrentQueue<IFileMeta>();
        //        long sentItems = 0;

        //        Func<IFileMeta, bool> send = fm =>
        //        {
        //            if (sentItems < bytesForeachSignal)
        //            {
        //                Interlocked.Add(ref sentItems, fm.Length);
        //                items.OnNext(fm);
        //                return true;
        //            }
        //            else
        //            {
        //                deferred.Enqueue(fm);
        //                return false;
        //            }
        //        };

        //        var sourceSub = source.Subscribe(fm =>
        //        {
        //            try
        //            {
        //                send(fm);
        //            }
        //            catch (Exception e)
        //            {
        //                items.OnError(e);
        //            }
        //        },
        //            error =>
        //            {
        //                items.OnError(error);
        //            }, onCompleted: () =>
        //            {
        //                items.OnCompleted();
        //            });


        //        var signalSub = signal.Subscribe(_ =>
        //        {
        //            try
        //            {
        //                Interlocked.Exchange(ref sentItems, 0);

        //                while (true)
        //                {
        //                    IFileMeta next;
        //                    if (deferred.TryDequeue(out next))
        //                    {
        //                        try
        //                        {
        //                            if (!send(next))
        //                                break;
        //                        }
        //                        catch (Exception e)
        //                        {
        //                            items.OnError(e);
        //                        }
        //                    }
        //                    else
        //                    {
        //                        if (deferred.IsEmpty)
        //                        {
        //                            break;
        //                        }
        //                    }
        //                }
        //            }
        //            catch (Exception e)
        //            {
        //                items.OnError(e);
        //            }
        //        }, error => items.OnError(error),
        //            () =>
        //            {
        //                items.OnCompleted();
        //                sourceSub.Dispose();
        //            });

        //        return new CompositeDisposable(new IDisposable[] { signalSub, sourceSub });

        //    });
        //}

        //public static IObservable<T> Throttle<T>(this IObservable<T> source, IObservable<Unit> signal, int initalPool, int itemsForEachSignal)
        //{
        //    if (initalPool < 1) throw new ArgumentException("must be greater then zero", "initalPool");

        //    var pool = source.Take(initalPool - 1);
        //    return source.Skip(initalPool - 1).Throttle(signal, itemsForEachSignal).Merge(pool);
        //}

        public static IObservable<IFileMeta[]> Batch(this IObservable<IFileMeta> source, int kbForEachBatch, TimeSpan? orBatchingTimeoutReached = null, IScheduler timeoutScheduler = null)
        {
            return Observable.Create<IFileMeta[]>(items =>
            {
                var deferred = new List<IFileMeta>();
                long sentItems = 0;
                IDisposable batchTimer = null;

                Action releaseBatch = () =>
                {
                    items.OnNext(deferred.ToArray());
                    deferred.Clear();
                    Interlocked.Exchange(ref sentItems, 0);
                };

                return source.Subscribe(fm =>
                {
                    deferred.Add(fm);
                    var total = Interlocked.Add(ref sentItems, fm.Length);

                    //do we have a full batch yet?
                    if (total < kbForEachBatch && orBatchingTimeoutReached.HasValue)
                    {
                        if (batchTimer != null)
                            batchTimer.Dispose();

                        batchTimer = Observable.Timer(orBatchingTimeoutReached.Value, timeoutScheduler ?? Scheduler.Default).Subscribe(_ => releaseBatch());
                        return;
                    }

                    if (batchTimer != null)
                        batchTimer.Dispose();

                    releaseBatch();
                },
                    error =>
                    {
                        items.OnError(error);
                    },
                    onCompleted: () =>
                    {
                        items.OnCompleted();
                    });
            });

        }
    }
}