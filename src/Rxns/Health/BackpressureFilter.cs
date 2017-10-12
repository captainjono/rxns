using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Rxns.Health
{
    /// <summary>
    /// WARNING: this class is untested
    /// 
    /// This class acts a man in the middle for a sequence. The idea is that once a sequence breaches a threshold,
    /// the system cannot cope wioth the backpressure and drastic action must be taken. The action can be anything
    /// from defering processing by storing the extra data in a persistant store, then re-processing them once the under
    /// threshold is reached, to spinning up a new worker thread to do the extra processing instead when ordering is
    /// not super important.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class BackpressureFilter<T>
    {
        private readonly Func<long, bool> _underThreadhold;
        private Func<long, bool> _overThreshold;

        private readonly Action<T> _over;
        private readonly Func<IEnumerable<T>> _under;
        private long _count;

        public BackpressureFilter(Func<long, bool> overThreshold, Action<T> over, Func<long, bool> underThreadhold, Func<IEnumerable<T>> under)
        {
            _overThreshold = overThreshold;
            _underThreadhold = underThreadhold;
            _over = over;
            _under = under;
        }

        /// <summary>
        /// Incrments the internal counter whenever something is observed
        /// </summary>
        /// <returns></returns>
        public FilterAction<T> Before()
        {
            return new FilterAction<T>()
            {
                When = _ =>
                {
                    //todo: properly count over and unders
                    Interlocked.Increment(ref _count);
                    return _overThreshold(_count);
                },
                Do = t =>
                {
                    _over(t);
                    return new T[] {};
                }
            };
        }

        /// <summary>
        /// Decrements the internal counter whenever somethign is observed
        /// </summary>
        /// <returns></returns>
        public FilterAction<T> After()
        {
            return new FilterAction<T>()
            {
                When = _ =>
                {
                    //todo: properly count over and unders

                    Interlocked.Decrement(ref _count);
                    return _underThreadhold(_count);
                },
                Do = t =>
                {
                    return _under().Concat(new [] { t });
                }
            };
        }
    }
}
