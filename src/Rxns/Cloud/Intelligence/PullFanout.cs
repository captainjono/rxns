using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading;
using System.Threading.Tasks;
using Rxns.Collections;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns.Cloud.Intelligence
{
    /// <summary>
    /// Pull-based fanout: workers pull next work item from a shared queue
    /// when their previous DoWork chain terminates. Atomic dequeue makes
    /// the "two dispatches arrive 60ms apart pick the same worker" race
    /// (phase 7n4) impossible by construction — only ONE waiting reader
    /// gets each item.
    ///
    /// <para>
    /// <b>Tag-aware routing.</b> Optional constructor takes two functions
    /// that map work and workers to "queue keys" (typically tag values).
    /// Items are routed to the queue named by their key; workers subscribe
    /// to one or more queue keys. A worker awaits all its subscribed
    /// semaphores via <see cref="Task.WhenAny(Task[])"/> with
    /// cancellation-on-winner so multi-tag workers wake on the first
    /// matching item. The default constructor (no funcs) routes everything
    /// to a single <c>_untagged</c> queue and every worker subscribes
    /// there — backward compatible with the original single-queue
    /// behaviour.
    /// </para>
    ///
    /// <para>
    /// Tag semantics are owned by the caller (BFG callsites compose
    /// <c>bfgTagWorkflow</c> into the queue-key funcs). The fanout itself
    /// just hashes by string key — it doesn't know about wildcards or
    /// match precedence. Caller responsibility:
    /// <list type="bullet">
    ///   <item>Untagged work goes to a key all workers subscribe to
    ///   (convention: <c>_untagged</c>).</item>
    ///   <item>Wildcard tags on a worker (e.g. <c>os:*</c>) → expand to the
    ///   set of concrete keys the worker accepts.</item>
    ///   <item>Multi-tag work → caller picks one queue (first-tag-wins);
    ///   workers subscribed to that tag pick it up.</item>
    /// </list>
    /// </para>
    /// </summary>
    public class PullFanout<T, TR> : IClusterFanout<T, TR> where TR : IRxn
    {
        /// <summary>
        /// Queue key used for items with no tag-routing function provided
        /// AND for items the caller's <see cref="_getWorkQueueKey"/> maps
        /// to null/empty. Workers SHOULD include this in their subscribed
        /// keys to receive default-routed work; the BFG composition does
        /// this automatically.
        /// </summary>
        public const string UntaggedQueueKey = "_untagged";

        public IDictionary<string, WorkerConnection<T, TR>> Workers { get; } =
            new UseConcurrentReliableOpsWhenCastToIDictionary<string, WorkerConnection<T, TR>>(
                new ConcurrentDictionary<string, WorkerConnection<T, TR>>());

        // Per-tag FIFO. Created lazily on first enqueue or worker subscribe.
        private readonly ConcurrentDictionary<string, ConcurrentQueue<T>> _queues =
            new ConcurrentDictionary<string, ConcurrentQueue<T>>();

        // Per-tag wake signal. Each Release wakes exactly one WaitAsync —
        // combined with atomic ConcurrentQueue.TryDequeue means exactly
        // one consumer per item.
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _signals =
            new ConcurrentDictionary<string, SemaphoreSlim>();

        // Per-worker cancellation so Dispose on a registration cleanly
        // stops its read loop without affecting other workers.
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _workerCts =
            new ConcurrentDictionary<string, CancellationTokenSource>();

        private readonly Func<T, string> _getWorkQueueKey;
        private readonly Func<IClusterWorker<T, TR>, IEnumerable<string>> _getWorkerQueueKeys;

        private Action<IRxn> _publish;

        /// <summary>Untagged: single shared queue, every worker subscribes.</summary>
        public PullFanout() : this(null, null) { }

        /// <summary>
        /// Tag-aware. <paramref name="getWorkQueueKey"/> maps each work item
        /// to the queue key it goes into (return null/empty for the
        /// untagged default). <paramref name="getWorkerQueueKeys"/> maps each
        /// registered worker to the set of queue keys it pulls from
        /// (typically the worker's tags + <see cref="UntaggedQueueKey"/>).
        /// </summary>
        public PullFanout(
            Func<T, string> getWorkQueueKey,
            Func<IClusterWorker<T, TR>, IEnumerable<string>> getWorkerQueueKeys)
        {
            _getWorkQueueKey = getWorkQueueKey ?? (_ => UntaggedQueueKey);
            _getWorkerQueueKeys = getWorkerQueueKeys ?? (_ => new[] { UntaggedQueueKey });
        }

        public void ConfigiurePublishFunc(Action<IRxn> publish) => _publish = publish;

        public void Fanout(T work)
        {
            var key = _getWorkQueueKey(work);
            if (string.IsNullOrEmpty(key)) key = UntaggedQueueKey;

            var (queue, signal) = EnsureQueue(key);
            queue.Enqueue(work);
            try
            {
                signal.Release();
            }
            catch (ObjectDisposedException)
            {
                $"PullFanout.Fanout: signal for key '{key}' disposed, dropping {typeof(T).Name}".LogDebug();
            }
        }

        // Lazily creates the queue + signal pair for a key. Called on both
        // dispatch (Fanout) and registration (RegisterWorker) so workers
        // subscribed to a key that hasn't been published to yet still have
        // a semaphore to await.
        private (ConcurrentQueue<T> queue, SemaphoreSlim signal) EnsureQueue(string key)
        {
            var q = _queues.GetOrAdd(key, _ => new ConcurrentQueue<T>());
            var s = _signals.GetOrAdd(key, _ => new SemaphoreSlim(0));
            return (q, s);
        }

        public IDisposable RegisterWorker(IClusterWorker<T, TR> worker)
        {
            var connection = new WorkerConnection<T, TR>() { Worker = worker };
            Workers.Add(worker.Name, connection);

            // Snapshot subscribed keys at registration. If the caller's
            // tag set changes mid-flight (unusual), they should re-register
            // the worker. Distinct() so duplicate keys don't double-await.
            var subscribedKeys = (_getWorkerQueueKeys(worker) ?? new[] { UntaggedQueueKey })
                .Where(k => !string.IsNullOrEmpty(k))
                .Distinct()
                .ToArray();
            if (subscribedKeys.Length == 0) subscribedKeys = new[] { UntaggedQueueKey };

            // Pre-create queues + signals so the read loop never races a
            // first-enqueue creation between WaitAsync and TryDequeue.
            foreach (var k in subscribedKeys) EnsureQueue(k);

            $"PullFanout.RegisterWorker: {worker.Name} route='{worker.Route}' subscribes=[{string.Join(",", subscribedKeys)}] pool={Workers.Count}".LogDebug();
            // Match CompeteFanout's existing line so existing scrape-based
            // integration tests + ops dashboards keep working unchanged.
            $"Worker registered, pool size {Workers.Count}".LogDebug();

            var cts = new CancellationTokenSource();
            _workerCts[worker.Name] = cts;

            _ = Task.Run(async () =>
            {
                try
                {
                    while (!cts.IsCancellationRequested)
                    {
                        var work = await TryTakeWork(subscribedKeys, cts.Token).ConfigureAwait(false);
                        if (work.IsEmpty) continue;          // canceled / no work matched
                        await RunDispatch(worker, work.Value, cts.Token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Normal shutdown — Dispose canceled the loop.
                }
                catch (Exception ex)
                {
                    $"PullFanout.read: {worker.Name} read loop terminated unexpectedly: {ex.GetType().Name}: {ex.Message}".LogDebug();
                }
            }, cts.Token);

            return Disposable.Create(() =>
            {
                $"PullFanout.RegisterWorker.Dispose: removing {worker.Name} pool={Workers.Count - 1}".LogDebug();
                Workers.Remove(worker.Name);
                if (_workerCts.TryRemove(worker.Name, out var c))
                {
                    try { c.Cancel(); } catch { /* idempotent */ }
                    c.Dispose();
                }
            });
        }

        // Awaits the first signal in the worker's subscribed set, then
        // atomically dequeues from that queue. Cancels the un-won
        // WaitAsyncs to avoid leaked semaphore decrements; if a sibling
        // wait completed before our cancel landed (lost race), the
        // semaphore decrement is compensated by Releasing it back.
        //
        // Returns Empty when no work was actually taken (race lost to
        // another worker, or canceled). Caller loops.
        private async Task<Maybe> TryTakeWork(string[] subscribedKeys, CancellationToken outer)
        {
            // C# 7.3-compatible `using` block (was `using var`) so this builds
            // for both netstandard2.0 and netstandard2.1 targets.
            using (var iterCts = CancellationTokenSource.CreateLinkedTokenSource(outer))
            {
                // One WaitAsync per subscribed key. Each is cancellable via iterCts.
                var tasks = new Task[subscribedKeys.Length];
                for (int i = 0; i < subscribedKeys.Length; i++)
                {
                    tasks[i] = _signals[subscribedKeys[i]].WaitAsync(iterCts.Token);
                }

                Task winner;
                try
                {
                    winner = await Task.WhenAny(tasks).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }

                // Cancel sibling waits so they unwind. Then collect them: any
                // that completed before the cancel landed (race) successfully
                // decremented their semaphore; we compensate by Releasing back.
                iterCts.Cancel();

                int winnerIdx = Array.IndexOf(tasks, winner);
                if (winnerIdx < 0)
                {
                    // Defensive — Task.WhenAny returned a task we don't have.
                    return Maybe.Empty;
                }

                for (int i = 0; i < tasks.Length; i++)
                {
                    if (i == winnerIdx) continue;
                    try
                    {
                        await tasks[i].ConfigureAwait(false);
                        // Sibling completed before cancel — decrement leaked,
                        // return it so other waiters / next iteration sees it.
                        _signals[subscribedKeys[i]].Release();
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected — cancel landed before WaitAsync completed,
                        // semaphore was NOT decremented for this task.
                    }
                }

                // Winner could itself be Faulted/Canceled if outer token fired
                // exactly when we were waking; treat as empty.
                if (winner.Status != TaskStatus.RanToCompletion) return Maybe.Empty;

                var key = subscribedKeys[winnerIdx];
                if (!_queues[key].TryDequeue(out var work))
                {
                    // Semaphore said "item available" but the queue is empty —
                    // means another worker raced us to TryDequeue and won.
                    // Caller loops; we don't compensate the semaphore (it was
                    // legitimately consumed by the producer's Release).
                    return Maybe.Empty;
                }

                return Maybe.Of(work);
            }
        }

        private async Task RunDispatch(IClusterWorker<T, TR> worker, T work, CancellationToken ct)
        {
            try
            {
                $"PullFanout.read: {worker.Name} took {typeof(T).Name}".LogDebug();
                await worker.DoWork(work)
                    .Do(r =>
                    {
                        $"PullFanout.read: {worker.Name} emitted {typeof(TR).Name} — publishing".LogDebug();
                        _publish(r);
                    })
                    .DefaultIfEmpty(default)
                    .LastOrDefaultAsync()
                    .ToTask(ct)
                    .ConfigureAwait(false);
                $"PullFanout.read: {worker.Name}.DoWork completed — competing for next".LogDebug();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Per-item failure must NOT kill the read loop. Log + loop.
                $"PullFanout.read: {worker.Name}.DoWork errored on {typeof(T).Name}: {ex.GetType().Name}: {ex.Message}".LogDebug();
            }
        }

        // Tiny option/maybe so TryTakeWork can disambiguate "got T" vs
        // "got nothing this iteration" without using nullable T (T is
        // unconstrained — could be a value type without HasValue).
        private readonly struct Maybe
        {
            public readonly bool IsEmpty;
            public readonly T Value;
            private Maybe(bool empty, T value) { IsEmpty = empty; Value = value; }
            public static Maybe Empty => new Maybe(true, default);
            public static Maybe Of(T v) => new Maybe(false, v);
        }
    }
}
