using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive.Threading.Tasks;
using System.Reflection;
using Rxns.DDD.Commanding;
using Rxns.Hosting;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns
{
    public static class Rxn
    {
        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="toExecute"></param>
        /// <param name="onError">Override the default error handling behaviour, which just passes through the exception on the Observables OnError channel</param>
        /// <param name="allowNull">By default, the observable sequence will onComplete if a null is returned. This flag will onNext the null before oncompleting it</param>
        /// <returns></returns>
        public static IObservable<T> Create<T>(Func<T> toExecute, Func<Exception, T> onError = null, bool allowNull = false)
        {
            return Observable.Create<T>(o =>
            {
                try
                {
                    var result = toExecute();

                    if (result != null || allowNull)
                        o.OnNext(result);
                }
                catch (Exception e)
                {
                    if (onError != null)
                        o.OnNext(onError(e));
                    else
                        o.OnError(e);
                }
                finally
                {
                    o.OnCompleted();
                }

                return Disposable.Empty;
            });
        }

        public static IObservable<T> Create<T>(Action toExecute, Func<Exception, T> onError = null)
        {
            return Observable.Create<T>(o =>
            {
                try
                {
                    toExecute();
                }
                catch (Exception e)
                {
                    if (onError != null)
                        o.OnNext(onError(e));
                    else
                        o.OnError(e);
                }
                finally
                {
                    o.OnCompleted();
                }

                return Disposable.Empty;
            });
        }

        public static IObservable<Unit> Create(Action toExecute, Func<Exception, Unit> onError = null)
        {
            return Observable.Create<Unit>(o =>
            {
                try
                {
                    toExecute();

                    o.OnNext(new Unit());
                }
                catch (Exception e)
                {
                    if (onError != null)
                        o.OnNext(onError(e));
                    else
                        o.OnError(e);
                }
                finally
                {
                    o.OnCompleted();
                }

                return Disposable.Empty;
            });
        }

        public static IObservable<T> Create<T>(Func<IObservable<T>> toExecute)
        {
            return Observable.Create<T>(o =>
            {
                try
                {
                    var result = toExecute();
                    if (result == null)
                    {
                        o.OnCompleted();
                        return Disposable.Empty;
                    }

                    return result.Subscribe(o);
                }
                catch (Exception e)
                {
                    o.OnError(e);
                    return Disposable.Empty;
                }
            });
        }

        public static IObservable<long> Create(TimeSpan repeats, IScheduler seconds = null)
        {
            return Observable.Timer(repeats, seconds ?? Scheduler.Default);
        }

        public static IObservable<T> Create<T>(TimeSpan repeats, Func<T> action, IScheduler seconds = null)
        {
            return Observable.Timer(repeats, seconds ?? Scheduler.Default).Select(_ => action());
        }

        public static IObservable<T> CreatePulse<T>(TimeSpan repeats, Func<T> action, IScheduler seconds = null)
        {
            return Observable.Timer(repeats, repeats, seconds ?? Scheduler.Default).Select(_ => action());
        }

        public static IObservable<IDisposable> Create(string pathToProcess, string args, Action<string> onInfo, Action<string> onError)
        {
            return Create<Process>(o =>
            {
                $"Starting {pathToProcess}".LogDebug();

                var reactorProcess = new ProcessStartInfo
                {

                    ErrorDialog = false,
                    WorkingDirectory = new FileInfo(pathToProcess).DirectoryName,
                    FileName = pathToProcess,
                    Arguments = args,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };

                var p = new Process
                {
                    StartInfo = reactorProcess,
                    EnableRaisingEvents = true,
                };

                var nameOfProcess = new FileInfo(pathToProcess).Name;

                p.Start();

                Rxn.DfrCreate(() => TaskObservableExtensions.ToObservable(p.StandardOutput.ReadLineAsync())).Do(msg =>
                {
                    onInfo(msg);
                }).DoWhile(() => !p.HasExited).Until(o.OnError);

                Rxn.DfrCreate(() => TaskObservableExtensions.ToObservable(p.StandardError.ReadLineAsync())).Do(msg =>
                {
                    onError(msg);
                }).DoWhile(() => !p.HasExited).Until(o.OnError);

                p.Exited += (__, _) =>
                {
                    o.OnError(new Exception($"{pathToProcess} exited"));
                };

                p.KillOnExit();

                var exit = new DisposableAction(() =>
                {
                    $"Stopping supervisor for {pathToProcess}".LogDebug();
                    p.Kill();
                    o.OnCompleted();
                });

                o.OnNext(p);

                return exit;
            });
        }

        public static IObservable<T> Create<T>(Func<IObserver<T>, IDisposable> toExecute)
        {
            return Observable.Create<T>(o =>
            {
                try
                {
                    return toExecute(o);
                }
                catch (Exception e)
                {
                    o.OnError(e);
                    return Disposable.Empty;
                }
            });
        }
        public static IObservable<T> DfrCreate<T>(Func<T> toExecute, Func<Exception, T> onError = null, bool allowNull = false)
        {
            return Observable.Defer(() => Create(toExecute, onError, allowNull));
        }

        public static IObservable<T> DfrCreate<T>(Func<IObservable<T>> toExecute)
        {
            return Observable.Defer(() => Create(toExecute));
        }

        public static IObservable<T> DfrCreate<T>(Func<IObserver<T>, IDisposable> toExecute)
        {
            return Observable.Defer(() => Create(toExecute));
        }

        public static IObservableQuery<T, TFilter> CreateViewFromQuery<T, TFilter>(ISubject<TFilter> filterStream, ContinuationToken token, Func<TFilter, ContinuationToken, IObservable<T>> queryFunc, IScheduler scheduler = null)
        {
            return new ObservableQuery<T, TFilter>(queryFunc, filterStream, token, scheduler);
        }

        public static IObservableQuery<Continuation<T[]>, TFilter> CreateViewFromQuery<T, TFilter, TEvent>(this IObservable<TEvent> stream, Func<TFilter, ContinuationToken, IObservable<Continuation<T[]>>> queryFunc, Func<T[]> store, ISubject<TFilter> filterStream, Func<TFilter, T, bool> filerFunc, ContinuationToken token, IScheduler scheduler = null, params Type[] eventsThatUpdateStore)
        {
            var view = CreateViewFromQuery(filterStream, token, queryFunc, scheduler).Subscribe();
            return CreateQueryableStore(stream, store, filterStream, filerFunc, token, e => eventsThatUpdateStore.Contains(e.GetType()), scheduler).Disposes(view);
        }

        public static IObservableQuery<Continuation<T[]>, TFilter> CreateViewFromQuery<T, TFilter, TEvent>(this IObservable<TEvent> stream, Func<TFilter, ContinuationToken, IObservable<Continuation<T[]>>> queryFunc, Func<T[]> store, ISubject<TFilter> filterStream, Func<TFilter, T, bool> filerFunc, ContinuationToken token, Func<TEvent, bool> updateWhen, IScheduler scheduler = null)
        {
            var view = CreateViewFromQuery(filterStream, token, queryFunc, scheduler);
            return CreateQueryableStore(stream, store, filterStream, filerFunc, token, updateWhen, scheduler).SyncWith(view);
        }

        public static IObservable<T[]> CreateQueryableStore1<T, TFilter, TEvent>(this IObservable<TEvent> stream, Func<TFilter, ContinuationToken, IObservable<Continuation<T[]>>> queryFunc, Func<T[]> store, ISubject<TFilter> filterStream, Func<TFilter, T, bool> filerFunc, ContinuationToken token, Func<TEvent, bool> updateWhen, IScheduler scheduler = null)
        {
            Func<TFilter, ContinuationToken, IObservable<T[]>> localQueryFunc =
                (f, p) => store()
                    .ToObservable()
                    .Select(allRecords =>
                    {
                        var currentTokenMeta = PagingToken.FromToken(p.Token, p.Size);
                        var pagedView = allRecords;

                        try
                        {
                            if (f != null) //apply filter if specified
                            {
                                pagedView = allRecords.Where(r => filerFunc(f, r)).ToArray();
                            }

                            if (p.Size.HasValue) //apply record limiter / pager if specified
                            {
                                var offset = currentTokenMeta.Index.HasValue ? currentTokenMeta.Index.Value - 1 : 0;
                                pagedView = pagedView.Skip(offset * p.Size.Value).Take(p.Size.Value).ToArray();
                            }
                        }
                        catch (Exception e)
                        {
                            Debug.WriteLine("QueryableStore Size or Filter function failure: {0}", e);
                        }

                        var total = pagedView.Length == 0 ? 0 : allRecords.Length + 1;
                        var nextPageToken = currentTokenMeta.Next(pagedView.Length, total);
                        var nextToken = p.Next(nextPageToken.ToString(), total, nextPageToken.HasMorePages());

                        return new Continuation<T[]>(pagedView, nextToken);
                    }).Where(w => w.Records.Length > 0).Select(_ => _.Records);

            var storeMutationEvents = stream.Where(updateWhen);
            var view = CreateViewFromQuery(filterStream, token, queryFunc, scheduler);
            var viewPipeline = view.Select(_ => _.Records).CombineLatest(storeMutationEvents, (a, b) => localQueryFunc(view.Filter.Value(), view.Continuation.Value())).SelectMany(q => q);

            return viewPipeline;
        }

        public static IObservableQuery<Continuation<T[]>, TFilter> CreateQueryableStore<T, TFilter, TEvent>(this IObservable<TEvent> stream, Func<T[]> store, ISubject<TFilter> filterStream, Func<TFilter, T, bool> filerFunc, ContinuationToken token, Func<TEvent, bool> updateWhen, IScheduler scheduler = null)
        {
            var storeObs = stream.Where(updateWhen).Select(_ => store());

            return new ObservableQuery<Continuation<T[]>, TFilter>(
                (f, p) => storeObs
                    .StartWith<T[]>(store())
                    .Select(allRecords =>
                    {
                        var currentTokenMeta = PagingToken.FromToken(p.Token, p.Size);
                        var pagedView = allRecords;

                        try
                        {
                            if (f != null) //apply filter if specified
                            {
                                pagedView = allRecords.Where(r => filerFunc(f, r)).ToArray();
                            }

                            if (p.Size.HasValue) //apply record limiter / pager if specified
                            {
                                var offset = currentTokenMeta.Index.HasValue ? currentTokenMeta.Index.Value - 1 : 0;
                                pagedView = pagedView.Skip(offset * p.Size.Value).Take(p.Size.Value).ToArray();
                            }
                        }
                        catch (Exception e)
                        {
                            Debug.WriteLine("QueryableStore Size or Filter function failure: {0}", e);
                        }

                        var total = pagedView.Length == 0 ? 0 : allRecords.Length + 1;
                        var nextPageToken = currentTokenMeta.Next(pagedView.Length, total);
                        var nextToken = p.Next(nextPageToken.ToString(), total, nextPageToken.HasMorePages());

                        return new Continuation<T[]>(pagedView, nextToken);
                    }).Where(w => w.Records.Length > 0),
                    filterStream,
                    token,
                    scheduler
                );
        }

        /// <summary>
        /// Gets property information for the specified <paramref name="property"/> expression.
        /// https://github.com/LeeCampbell/RxCookbook/blob/master/Model/PropertyChange.md
        /// </summary>
        /// <typeparam name="TSource">Type of the parameter in the <paramref name="property"/> expression.</typeparam>
        /// <typeparam name="TValue">Type of the property's value.</typeparam>
        /// <param name="property">The expression from which to retrieve the property information.</param>
        /// <returns>Property information for the specified expression.</returns>
        /// <exception cref="ArgumentException">The expression is not understood.</exception>
        public static PropertyInfo GetPropertyInfo<TSource, TValue>(this Expression<Func<TSource, TValue>> property)
        {
            PropertyInfo propertyInfo;
            if (property == null) throw new ArgumentNullException("property");
            var body = property.Body as MemberExpression;

            if (body != null) //simple case
            {
                propertyInfo = body.Member as PropertyInfo;
                if (propertyInfo == null) throw new ArgumentException("Expression is not a property", "property");

                return propertyInfo;
            }

            //expression may be boxed because it was cast
            var unary = property.Body as UnaryExpression;
            if (unary == null) throw new ArgumentException("Expression is not a property", "property");

            propertyInfo = (unary.Operand as MemberExpression).Member as PropertyInfo;
            if (propertyInfo == null) throw new ArgumentException("Expression is not a property", "property");

            return propertyInfo;
        }

        public static IObservable<IRxn> Empty()
        {
            return Observable.Empty<IRxn>();
        }

        public static IObservable<T> Empty<T>()
        {
            return Observable.Empty<T>();
        }

        /// <summary>
        /// A timer that is capable of being paused and resumed. It does not maintain the long value of the sequence
        /// between pauses because i have never found  use for this value ;)
        /// 
        /// limitation: when the timer is paused and resumed, it starts producing values again straight away, if even if the period has not elpased yet
        /// </summary>
        /// <param name="dueTime"></param>
        /// <param name="period"></param>
        /// <param name="isPaused"></param>
        /// <param name="scheduler"></param>
        /// <returns></returns>
        public static IObservable<long> TimerWithPause(DateTimeOffset dueTime, TimeSpan period, IObservable<bool> isPaused, IScheduler scheduler = null)
        {
            return Rxn.DfrCreate<long>(o =>
            {
                var isFirst = true;
                IDisposable timer = null;

                Func<bool, IObservable<long>> createTimer = (isInital) =>
                {
                    return Observable.Timer(isInital ? dueTime : DateTimeOffset.MinValue, period, scheduler ?? Scheduler.Default).Do(v => o.OnNext(v), error => o.OnError(error));
                };

                var playPauser = isPaused.If(paused => paused, _ =>
                {
                    if(timer != null)
                        timer.Dispose();
                })
                .If(paused => !paused, _ =>
                {
                    timer = createTimer(isFirst).Subscribe();
                    isFirst = false;
                })
                .Subscribe();

                return new CompositeDisposable(timer, playPauser);
            });
        }

        public static IObservable<IRxnAppContext> Create(IRxnHostableApp app, IRxnHost host, IRxnAppCfg cfg)
        {
            return host.Run(app, cfg);
        }

        public static IMicroApp ToRxnApp<T>(this IObservable<T> rxn, string[] args) where T : IDisposable
        {
            return new RxnMicroApp(Rxn.Create<IDisposable>(o =>
            {
                return rxn.Subscribe(d => { o.OnNext(d); }, e => o.OnError(e), o.OnCompleted);
            }), args);
        }

        public static IRxnApp WithRxns(this IMicroApp context, IRxnDef def)
        {
            return new RxnApp(context, def, new RxnAppFactory());
        }

        public static IRxnApp WithRxns(this Type context, IRxnDef def)
        {
            return new RxnApp(context, def, new RxnAppFactory());
        }

        public static IRxnHostableApp Named(this IRxnApp app, IRxnAppInfo appInfo)
        {
            return new RxnHostableApp(app, appInfo);
        }

        public static IObservable<IRxnAppContext> OnHost(this IRxnHostableApp app, IRxnHost host, IRxnAppCfg cfg)
        {
            return host.Run(app, cfg);
        }


        public static IObservable<T> On<T>(IScheduler scheduler, Func<T> operation)
        {
            return Observable.Start(operation, scheduler);
        }

        public static IObservable<Unit> On(IScheduler scheduler, Action operation)
        {
            return Observable.Start(operation, scheduler);
        }

        /// <summary>
        /// this will iterate an array in sequence serially until completion of first failure
        /// </summary>
        /// <param name="items"></param>
        /// <param name="selector"></param>
        /// <typeparam name="T"></typeparam>
        /// <typeparam name="R"></typeparam>
        /// <returns></returns>
        public static IObservable<T> SelectMany<T, R>(this R[] items, Func<R, IObservable<T>> selector)
        {
            return Rxn.Create<T>(o =>
            {
                var current = 0;
                bool isCanceled = false;
                Action trampolineIterate = null;
                var currentSelector = Disposable.Empty;

                trampolineIterate = () => currentSelector = selector(items[current++]).Subscribe(
                    result => { o.OnNext(result); },
                    onError => { o.OnError(onError); },
                    () =>
                    {
                        if (current == items.Length || isCanceled)
                        {
                            o.OnCompleted();
                        }
                        else
                        {
                            CurrentThreadScheduler.Instance.Schedule(() => trampolineIterate());
                        }
                    });

                trampolineIterate();

                return Disposable.Create(() =>
                {
                    currentSelector.Dispose();
                    isCanceled = true;
                });
            });
        }


        /// <summary>
        /// If a sequence sometimes throws exceptions, this function will catch that
        /// error and recreate the sequence while maintaining all existing subscriptions
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="unreliable"></param>
        /// <param name="onError"></param>
        /// <returns></returns>
        public static IObservable<T> MakeReliable<T>(Func<IObservable<T>> unreliable, Action<Exception> onError)
        {
            Func<IObservable<T>> reliable = null;

            reliable = () => unreliable()
                .Catch<T, Exception>(error =>
                {
                    onError(error);
                    return reliable();
                });

            return reliable();
        }


        public static IObservable<T> CatchAndSignal<T>(this IObservable<T> source, Action signal, Action<Exception> error)
        {
            return source.Catch<T, Exception>(e =>
            {
                error(e);
                CurrentThreadScheduler.Instance.Schedule(() => signal());
                return Observable.Empty<T>();
            });
        }

        public static IObservable<long> Then(this TimeSpan when, bool repeat = false, IScheduler schedler = null)
        {
            if (schedler != null)
                return repeat ? Observable.Timer(when, when, schedler) : Observable.Timer(when, schedler);

            return repeat ? Observable.Timer(when, when) : Observable.Timer(when);
        }
    }
}
