using System;
using System.Collections.Generic;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;

namespace Rxns
{
    public static class BufferUntilSizeExtensions
    {
        /// <summary>
        /// Like <c>Buffer(timeSpan, count)</c> but the cap is BYTES-based,
        /// computed via <paramref name="sizeOf"/>. Emits a batch when EITHER:
        /// <list type="bullet">
        ///   <item>cumulative size of the in-flight batch + the next item would
        ///         exceed <paramref name="maxBatchBytes"/> (flushes BEFORE
        ///         appending so the batch stays under cap),</item>
        ///   <item>or <paramref name="maxLatency"/> elapses since the first
        ///         item in the current batch.</item>
        /// </list>
        /// A single item larger than the cap is emitted alone — caller still
        /// has to handle that case (truncate at producer or accept rejection).
        /// </summary>
        public static IObservable<IList<T>> BufferUntilSize<T>(
            this IObservable<T> source,
            Func<T, int> sizeOf,
            int maxBatchBytes,
            TimeSpan maxLatency,
            IScheduler scheduler = null)
        {
            scheduler = scheduler ?? Scheduler.Default;
            return Observable.Create<IList<T>>(observer =>
            {
                var gate = new object();
                var buf = new List<T>();
                var bufBytes = 0;
                IDisposable timer = Disposable.Empty;

                void Emit()
                {
                    List<T> batch = null;
                    lock (gate)
                    {
                        if (buf.Count == 0) return;
                        batch = buf;
                        buf = new List<T>();
                        bufBytes = 0;
                        timer.Dispose();
                        timer = Disposable.Empty;
                    }
                    observer.OnNext(batch);
                }

                var sub = source.Subscribe(item =>
                {
                    int itemBytes = 0;
                    try { itemBytes = Math.Max(0, sizeOf(item)); } catch { }

                    bool emitBefore = false;
                    bool emitAfter = false;
                    lock (gate)
                    {
                        // If the running batch already has items AND the new item
                        // would push us over the cap, flush the running batch
                        // first, then start a new one.
                        if (buf.Count > 0 && bufBytes + itemBytes > maxBatchBytes)
                            emitBefore = true;
                    }
                    if (emitBefore) Emit();

                    lock (gate)
                    {
                        if (buf.Count == 0)
                        {
                            // First item in a fresh batch — arm the latency timer.
                            timer = scheduler.Schedule(maxLatency, Emit);
                        }
                        buf.Add(item);
                        bufBytes += itemBytes;

                        // Single-item-larger-than-cap: emit alone immediately.
                        if (bufBytes > maxBatchBytes && buf.Count == 1)
                            emitAfter = true;
                    }
                    if (emitAfter) Emit();
                },
                ex =>
                {
                    Emit();
                    observer.OnError(ex);
                },
                () =>
                {
                    Emit();
                    observer.OnCompleted();
                });

                return new CompositeDisposable(sub, Disposable.Create(() =>
                {
                    timer.Dispose();
                    Emit();
                }));
            });
        }
    }
}
