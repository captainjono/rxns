using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;

namespace Rxns.Scheduling
{
    public static class ObservableExtensions
    {
        /// <summary>
        /// Returns a value only when all the groups IsRunning condition is
        /// the same as the input value
        /// </summary>
        /// <param name="groups">The groups to watch</param>
        /// <param name="isRunning">The state to watch for</param>
        /// <returns>When the state is true for all groups</returns>
        public static IObservable<bool> WhenAllRunning(this IEnumerable<IAmStateful> groups, bool isRunning)
        {
            if (!groups.Any())
                return Observable.Return(true);

            return groups.Select(g => g.IsRunning)
                .CombineLatest(a => a.All(isrunning => isrunning == isRunning)) //make sure all are not running
                .SkipWhile(cond => !cond)
                .FirstAsync(); //only produce a value when all arnt running
        }

        public static IObservable<T> CancelsWith<T>(this IObservable<T> task, IObservable<T> cancelObservable, T cancelationValue)
        {
            return Observable.Create<T>(o =>
            {
                var c = new CompositeDisposable();

                cancelObservable.Subscribe(value =>
                {
                    if (value.Equals(cancelationValue))
                        o.OnError(new TaskCanceledException("Cancelled"));
                }).DisposedBy(c);

                return task.Subscribe(o).DisposedBy(c); 
            });
        }


        public static IObservable<T> CancelsWith<T>(this IObservable<T> task, IObservable<T> cancelObservable, Predicate<T> whereClause = null)
        {
            return Observable.Create<T>(o =>
            {
                var c = new CompositeDisposable();

                cancelObservable.Subscribe(value =>
                {
                    if (whereClause == null || whereClause(value))
                        o.OnError(new TaskCanceledException("Cancelled"));
                }).DisposedBy(c);

                return task.Subscribe(o).DisposedBy(c); 
            });
        }

        public static T Value<T>(this IObservable<T> item)
        {
            return item.Take(1).Wait();
        }

        public static void SetValue<T>(this BehaviorSubject<T> item, T newValue)
        {
            item.OnNext(newValue);
        }

        public static IDisposable SubscribeWeakly<T, TTarget>(this IObservable<T> observable, TTarget target, Action<TTarget, T> onNext) where TTarget : class
        {
            var reference = new WeakReference(target);

            if (onNext.Target != null)
            {
                throw new ArgumentException("onNext must refer to a static method, or else the subscription will still hold a strong reference to target");
            }

            IDisposable subscription = null;
            subscription = observable.Subscribe(item =>
            {
                var currentTarget = reference.Target as TTarget;
                if (currentTarget != null)
                {
                    onNext(currentTarget, item);
                }
                else
                {
                    subscription.Dispose();
                }
            });

            return subscription;
        }
    }
}
