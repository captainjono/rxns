using System;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;

namespace Rxns.Health
{


    /// <summary>
    /// Monitors a specific set of operations in a sequence for a situation where
    /// new values are observed faster then the operation can process them. This is also
    /// known as backpressure.
    /// 
    /// Always hookup both ToProcess and Processed() triggers to monitor calls around the
    /// operations you want to monitor for this situation.
    /// 
    /// ie.  New up the classs then feed
    /// IObservable.
    ///     .Monitor(this.ToProcess())
    ///     .Operation(s)ToMonitor()
    ///     .Monitor(this.Processed())
    ///     .Subscribe()
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class BackpressureAction<T> : IDisposable, IMonitorActionFactory<T>
    {
        /// <summary>
        /// The current amount of backpressure
        /// </summary>
        public long Current { get { return _count; } }
        private long _count;
        private readonly Func<long, bool> _threshold;
        private readonly Action<T, long> _do;
        private Subject<T> doBuffer = new Subject<T>();


        public BackpressureAction(Func<long, bool> threshold, Action<T, long> @do, TimeSpan doEvery, IScheduler doEveryScheduler = null)
        {
            _threshold = threshold;
            _do = @do;
            doEveryScheduler = doEveryScheduler ?? Scheduler.Default;

            doBuffer.Throttle(doEvery, doEveryScheduler).Do(t => _do(t, Current)).Until();
        }

        /// <summary>
        /// Incrments the internal counter whenever something is observed
        /// </summary>
        /// <returns></returns>
        public MonitorAction<T> Before()
        {
            return new MonitorAction<T>()
            {
                When = _ =>
                {
                    Interlocked.Increment(ref _count);

                    return _threshold(_count);
                },
                Do = t =>
                {
                    doBuffer.OnNext(t);
                }
            };
        }

        /// <summary>
        /// Decrements the internal counter whenever somethign is observed
        /// </summary>
        /// <returns></returns>
        public MonitorAction<T> After()
        {
            return new MonitorAction<T>()
            {
                When = _ =>
                {
                    Interlocked.Decrement(ref _count);
                    return false;
                }
            };
        }

        public void Dispose()
        {
            doBuffer.Dispose();
        }
    }

    /// <summary>
    /// Monitors a specific set of operations in a sequence for a situation where
    /// new values are observed faster then the operation can process them. This is also
    /// known as backpressure.
    /// 
    /// Always hookup both ToProcess and Processed() triggers to monitor calls around the
    /// operations you want to monitor for this situation.
    /// 
    /// ie.  New up the classs then feed
    /// IObservable.
    ///     .Monitor(this.ToProcess())
    ///     .Operation(s)ToMonitor()
    ///     .Monitor(this.Processed())
    ///     .Subscribe()
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class StreamTimerAction<T> : IDisposable, IMonitorActionFactory<T>
    {
        private long _count;
        private Subject<long> _timingBuffer = new Subject<long>();

        public StreamTimerAction(Action<long> onSample, TimeSpan sampleTime, IScheduler doEveryScheduler = null)
        {
            doEveryScheduler = doEveryScheduler ?? Scheduler.Default;
            _timingBuffer.Buffer(sampleTime, doEveryScheduler).Select(_ => Interlocked.Exchange(ref _count, 0)).Do(onSample).Until();
        }

        /// <summary>
        /// Incrments the internal counter whenever something is observed
        /// </summary>
        /// <returns></returns>
        public MonitorAction<T> Before()
        {
            return new MonitorAction<T>()
            {
                When = _ =>
                {
                    Interlocked.Increment(ref _count);
                    return false;
                }
            };
        }

        /// <summary>
        /// Decrements the internal counter whenever somethign is observed
        /// </summary>
        /// <returns></returns>
        public MonitorAction<T> After()
        {
            return new MonitorAction<T>()
            {
                When = _ => false
            };
        }

        public void Dispose()
        {
            _timingBuffer.Dispose();
        }
    }
}
