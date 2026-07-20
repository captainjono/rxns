using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading.Tasks;

namespace System.Reactive
{
    public static class ObservableExtensions
    {
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
        public static void SetValue<T>(this BehaviorSubject<T> item, T newValue)
        {
            item.OnNext(newValue);
        }
    }
}
