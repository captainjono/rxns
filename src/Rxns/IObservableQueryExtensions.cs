using System;
using System.Reactive.Subjects;
using Rxns.DDD.Commanding;
using Rxns.Interfaces;

namespace Rxns
{
    public static class IObservableQueryExtensions
    {
        public static IObservableQuery<T, TFilter> FilterWith<T, TFilter>(this IObservableQuery<T, TFilter> context, ISubject<TFilter> filter)
        {
            if (filter == context.Filter) return context;
            filter.Subscribe(context.Filter).DisposedBy(context);

            return context;
        }

        public static IObservableQuery<T, TFilter> LimitWith<T, TFilter>(this IObservableQuery<T, TFilter> context, IObservable<ContinuationToken> limiter)
        {
            if (limiter == context.Continuation) return context;
            limiter.Subscribe(context.Continuation).DisposedBy(context);

            return context;
        }
    }
}
