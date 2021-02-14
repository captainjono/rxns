## Rxn decorators

These are components which add `event sourcing` to any existing API without needing to recompile them.

They have a variety of uses including: 

### RxnDecorators

```c#
/// <summary>
/// An class which aguments another type, adding an event sourcing flavour
/// to its behaviour.
/// 
/// "in reaction to this event, do this operation on the decorated class"
/// </summary>
/// <typeparam name="TEvents"></typeparam>
/// <typeparam name="TDecorated"></typeparam>
public class RxnDecorator<TEvents, TDecorated> : ReportsStatus, IObservable<TEvents>
{
    private readonly Action<TEvents, TDecorated>[] _transformations;
    private readonly TDecorated _decorated;
    private readonly Subject<Unit> _onUpdated;
    private readonly IObservable<TEvents> _events;
    
    /// <summary>
    /// Occours when the a transformer has applied a change to the instance
    /// </summary>
    public IObservable<Unit> OnUpdated { get { return _onUpdated; } }

    /// <summary>
    /// Decorates a type with event sourcing sauce 
    /// </summary>
    /// <param name="decorated">An instance of the type to decorate</param>
    /// <param name="events">The stream that drives the transformers</param>
    /// <param name="transformations">A series of transformers, that mutate the decorated instance based on a specific rxn</param>
    public RxnDecorator(TDecorated decorated, IObservable<TEvents> @events, params Action<TEvents, TDecorated>[] transformations)

```

### ExpiringCacheDecorator  

```c#

/// <summary>
/// A cache decorator which expires keys after a non-sliding timespan.
/// The GetOrLookup operations are thread-safe with the cleaning of the cache
/// on a per-key basis
/// </summary>
/// <typeparam name="TObj"></typeparam>
/// <typeparam name="TKey"></typeparam>
/// <typeparam name="TValue"></typeparam>
public class ExpiringCacheDecorator<TObj, TKey, TValue> : IExpiringCache<TKey, TValue>

```

### LazyCacheDecorator

```c#
  /// <summary>
/// This decorator is used to lazily persist information in the dictionary to a store. 
/// Each operation is performed in memory and then queued to be written to the database in an async reliable fashion.
/// </summary>
/// <typeparam name="TValue">The key for the dictionary is always a string, this is the type of the value of the dictionary</typeparam>
public abstract class LazyCacheDecorator<TValue, TStore, TStoreRecord> : ReportsStatus, IDictionary<string, TValue>, IReportHealth 

        public ExpiringCacheDecorator(
        TObj decorated, 
        Func<TObj, TKey, TValue> getter, Action<TObj, TKey, TValue> setter,
        Func<TObj, TKey, bool> exists, Action<TObj, TKey> removeFunc, 
        TimeSpan expiration, 
        TimeSpan? lockTime = null,
        IScheduler cleanupSchedulder = null)
    {
```