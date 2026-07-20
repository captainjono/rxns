using System;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;

namespace Rxns.Health
{
    public class StreamCounterPulsar<T> : IDisposable, IMonitorActionFactory<T>
    {
        private long _count;
        private readonly Subject<long> _timingBuffer = new Subject<long>();

        public StreamCounterPulsar(Action<long> onSample, TimeSpan sampleTime, IScheduler doEveryScheduler = null)
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
