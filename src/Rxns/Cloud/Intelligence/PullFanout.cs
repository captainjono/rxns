using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Rxns.Collections;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns.Cloud.Intelligence
{
    /// <summary>
    /// Hands work to workers one decision at a time, on a single pump.
    ///
    /// <para><b>Why a pump and not a race.</b> Workers used to compete: each ran its own read loop,
    /// parked on a semaphore, and whoever woke first took the item. That makes every property of the
    /// system emergent. Who gets the next item is decided by a lock's wait queue, which nothing can
    /// read; whether work spreads over machines depends on which thread the pool happened to
    /// schedule; and a fairness rule can only be expressed as a worker declining its turn and hoping
    /// a better one is awake to take it. It is also untestable except by sleeping and hoping.</para>
    ///
    /// <para>Here, every decision happens on one <see cref="EventLoopScheduler"/>. There is no
    /// concurrency to reason about inside it, so the ordering is not a side effect of a primitive -
    /// it is <see cref="_idle"/>, a list, in the order workers became free. Exactly one consumer per
    /// item is likewise structural rather than argued: only the pump dequeues, and the pump is one
    /// thread. The race this class was originally written to close - two dispatches 60ms apart
    /// picking the same worker - cannot be expressed here.</para>
    ///
    /// <para><b>Fairness is a choice, not a delay.</b> The pump picks the idle worker whose capacity
    /// unit has been given the least work so far, ties going to whoever has been waiting longest. No
    /// timer, no grace period, no worker giving up its turn. Adding a machine adds capacity, which is
    /// the only reason to add one - and when that stopped being true, nothing could say so, because
    /// the previous answer lived in a semaphore's wait queue.</para>
    ///
    /// <para><b>Tag-aware routing.</b> Optional constructor takes two functions that map work and
    /// workers to "queue keys" (typically tag values). Items are queued under the key they name;
    /// a worker is only offered items from the keys it subscribes to. The default constructor routes
    /// everything to <see cref="UntaggedQueueKey"/> and every worker subscribes there.</para>
    ///
    /// <para>Tag semantics are owned by the caller (callsites compose <c>bfgTagWorkflow</c> into the
    /// queue-key funcs). The fanout itself just groups by string key - it does not know about
    /// wildcards or match precedence. Caller responsibility:
    /// <list type="bullet">
    ///   <item>Untagged work goes to a key all workers subscribe to (convention:
    ///   <c>_untagged</c>).</item>
    ///   <item>Wildcard tags on a worker (e.g. <c>os:*</c>) expand to the set of concrete keys the
    ///   worker accepts.</item>
    ///   <item>Multi-tag work: the caller picks one queue (first-tag-wins).</item>
    /// </list>
    /// </para>
    /// </summary>
    public class PullFanout<T, TR> : IClusterFanout<T, TR>, IDisposable where TR : IRxn
    {
        /// <summary>
        /// Queue key used for items with no tag-routing function provided AND for items the caller's
        /// <see cref="_getWorkQueueKey"/> maps to null/empty. Workers SHOULD include this in their
        /// subscribed keys to receive default-routed work.
        /// </summary>
        public const string UntaggedQueueKey = "_untagged";

        public IDictionary<string, WorkerConnection<T, TR>> Workers { get; } =
            new UseConcurrentReliableOpsWhenCastToIDictionary<string, WorkerConnection<T, TR>>(
                new ConcurrentDictionary<string, WorkerConnection<T, TR>>());

        // Enqueued from whichever thread dispatched; dequeued only on the pump.
        private readonly ConcurrentDictionary<string, ConcurrentQueue<T>> _work =
            new ConcurrentDictionary<string, ConcurrentQueue<T>>();

        // Workers waiting for something to do, oldest wait first. Pump-only, so a plain list is
        // correct and - the point of the rewrite - readable: the order work will be handed out in is
        // a value this class holds, not a property of a lock nobody can inspect.
        private readonly LinkedList<Reg> _idle = new LinkedList<Reg>();

        // How much each capacity unit has been given over the run, and how much it is running now.
        // Pump-only. Cumulative rather than in-flight because work that spreads only while a machine
        // is busy does not spread at all when items are short: the first machine is idle again before
        // the next item arrives and legitimately wins. Load generation wants the opposite - each item
        // is a machine's worth of load, so it should land somewhere new whether or not the last one
        // has finished.
        private readonly Dictionary<string, int> _taken = new Dictionary<string, int>();
        private readonly Dictionary<string, int> _running = new Dictionary<string, int>();

        private readonly Func<T, string> _getWorkQueueKey;
        private readonly Func<IClusterWorker<T, TR>, IEnumerable<string>> _getWorkerQueueKeys;
        private readonly Func<IClusterWorker<T, TR>, string> _getWorkerMachine;

        // Stable per-instance id: GetHashCode can collide, and "is this the same fanout?" is exactly
        // the question these diagnostics exist to answer.
        private readonly string _instanceId = Guid.NewGuid().ToString("N").Substring(0, 6);

        // Every decision this class makes is posted here and runs on the other side of one
        // ObserveOn. Rx guarantees an observer is never called concurrently, so that single
        // subscription IS the serialization - there is no lock in this class because there is
        // nothing two threads can both be inside.
        private readonly ISubject<Action> _pump = Subject.Synchronize(new Subject<Action>());

        // Its own thread, not the thread pool. This decides every dispatch in the process, and the
        // pool is precisely what is contended when a fleet is being handed a run - queueing the
        // decision behind whatever else the pool is doing is how a dispatch arrives late for a
        // reason that has nothing to do with the fleet. One thread per fanout, and an arena has one
        // fanout; a caller that never disposes keeps it, which is what a process-long dispatcher
        // wants anyway.
        private readonly EventLoopScheduler _decisions = new EventLoopScheduler();
        private readonly ISubject<string> _parked = Subject.Synchronize(new Subject<string>());
        private readonly CompositeDisposable _resources = new CompositeDisposable();

        private Action<IRxn> _publish;
        private volatile bool _disposed;

        /// <summary>
        /// Fires with a worker's name each time it joins the idle list and can be handed the next
        /// item.
        ///
        /// <para>Registering a worker is not the same as that worker being ready - registration is a
        /// message to the pump, and the pump acts on it in its own time. Anything reasoning about
        /// where an item will land has to wait for this rather than for the call to
        /// <see cref="RegisterWorker"/> to return, or it is reasoning about a fleet that is not
        /// listening yet.</para>
        /// </summary>
        public IObservable<string> WorkerParked => _parked;

        /// <summary>Untagged: one shared queue, every worker eligible.</summary>
        public PullFanout() : this(null, null) { }

        /// <summary>
        /// Tag-aware. <paramref name="getWorkQueueKey"/> maps each work item to the queue key it goes
        /// into (null/empty for the untagged default). <paramref name="getWorkerQueueKeys"/> maps
        /// each registered worker to the set of keys it pulls from.
        /// </summary>
        public PullFanout(
            Func<T, string> getWorkQueueKey,
            Func<IClusterWorker<T, TR>, IEnumerable<string>> getWorkerQueueKeys)
            : this(getWorkQueueKey, getWorkerQueueKeys, null)
        {
        }

        /// <summary>
        /// As above, plus <paramref name="getWorkerMachine"/>: which physical host a worker runs on.
        ///
        /// <para>Supply it when several worker processes share a machine and the point of adding
        /// machines is to add capacity. Without it the fanout cannot tell four processes on one VM
        /// from four VMs - measured on a two-VM cluster, one VM ran the entire dispatch while the
        /// other sat idle with both its workers registered and free, so asking for two machines'
        /// worth of load quietly got one.</para>
        ///
        /// <para>Null is not a loss of fairness, only of grouping: a worker that names no machine is
        /// treated as its own capacity unit, so a fleet of plain workers still goes round them in
        /// turn.</para>
        /// </summary>
        public PullFanout(
            Func<T, string> getWorkQueueKey,
            Func<IClusterWorker<T, TR>, IEnumerable<string>> getWorkerQueueKeys,
            Func<IClusterWorker<T, TR>, string> getWorkerMachine)
        {
            _getWorkQueueKey = getWorkQueueKey ?? (_ => UntaggedQueueKey);
            _getWorkerQueueKeys = getWorkerQueueKeys ?? (_ => new[] { UntaggedQueueKey });
            _getWorkerMachine = getWorkerMachine;

            _resources.Add(_pump.ObserveOn(_decisions).Subscribe(decide => decide()));
            _resources.Add(_decisions);
        }

        public void ConfigiurePublishFunc(Action<IRxn> publish) => _publish = publish;

        public void Fanout(T work)
        {
            var key = _getWorkQueueKey(work);
            if (string.IsNullOrEmpty(key)) key = UntaggedQueueKey;

            // Queued under its key and no further. Work used to be bound to a specific machine here,
            // at enqueue, which spread it evenly and then could not revisit the decision: a machine
            // that joined afterwards found its own queue empty and the shared one already emptied, so
            // it sat idle beside work it could have run. On a hundred-device run that cost half the
            // fleet - fifty machines finished booting two minutes in and were given nothing. Where
            // work runs is now settled when a worker is actually free to run it.
            _work.GetOrAdd(key, _ => new ConcurrentQueue<T>()).Enqueue(work);
            OnPump(Drain);
        }

        public IDisposable RegisterWorker(IClusterWorker<T, TR> worker)
        {
            var connection = new WorkerConnection<T, TR> { Worker = worker };
            Workers.Add(worker.Name, connection);

            var machine = _getWorkerMachine?.Invoke(worker);

            // Snapshot subscribed keys at registration. A caller whose tag set changes mid-flight
            // should re-register the worker. Distinct() so a duplicate key is not offered twice.
            var keys = (_getWorkerQueueKeys(worker) ?? new[] { UntaggedQueueKey })
                .Where(k => !string.IsNullOrEmpty(k))
                .Distinct()
                .ToArray();
            if (keys.Length == 0) keys = new[] { UntaggedQueueKey };

            var reg = new Reg
            {
                Worker = worker,
                Connection = connection,
                Keys = keys,
                // A worker that names no machine is its own capacity unit, so it neither starves
                // others nor is starved by a rule it never opted into.
                Unit = string.IsNullOrEmpty(machine) ? "worker:" + worker.Name : machine
            };

            $"PullFanout.RegisterWorker: {worker.Name} machine='{machine ?? "(none)"}' unit='{reg.Unit}' route='{worker.Route}' subscribes=[{string.Join(",", keys)}] instance={_instanceId}".LogDebug();

            var subscription = new CompositeDisposable(reg.Running);
            subscription.Add(Disposable.Create(() => OnPump(() => Unregister(reg))));

            OnPump(() =>
            {
                MakeIdle(reg);
                Drain();
            });

            return subscription;
        }

        /// <summary>
        /// Hand out as much as can be handed out right now, then stop. Runs only on the pump.
        /// </summary>
        private void Drain()
        {
            while (TryAssign())
            {
            }
        }

        /// <summary>
        /// Pick the best idle worker with something to do, and give it one item.
        ///
        /// <para>Best is the capacity unit that has been given the least so far; ties go to whoever
        /// has been idle longest, which is the FIFO the semaphore's wait queue used to provide
        /// invisibly. Written out as a scan because the list is short, and because a rule you can
        /// read beats a rule you can only observe.</para>
        /// </summary>
        private bool TryAssign()
        {
            Reg best = null;
            string bestKey = null;
            var fewest = int.MaxValue;

            foreach (var reg in _idle)
            {
                var key = FirstKeyWithWork(reg);
                if (key == null) continue;

                var taken = Count(_taken, reg.Unit);
                if (taken >= fewest) continue;   // strict, so the earliest idle worker wins a tie

                best = reg;
                bestKey = key;
                fewest = taken;
            }

            if (best == null) return false;
            if (!_work[bestKey].TryDequeue(out var work)) return false;

            Assign(best, work);
            return true;
        }

        private string FirstKeyWithWork(Reg reg)
        {
            foreach (var key in reg.Keys)
                if (_work.TryGetValue(key, out var q) && !q.IsEmpty)
                    return key;

            return null;
        }

        private void Assign(Reg reg, T work)
        {
            _idle.Remove(reg.Node);
            reg.Node = null;

            // Owned by the fanout and set before anything async happens, so a second decision cannot
            // see this worker as free. It is the synchronous counterpart to IClusterWorker.IsBusy,
            // which only publishes once the worker has actually started.
            reg.Connection.IsReserved = true;
            Bump(_taken, reg.Unit, 1);
            Bump(_running, reg.Unit, 1);

            $"PullFanout.assign: {reg.Worker.Name} on {reg.Unit} took {typeof(T).Name} (taken={Count(_taken, reg.Unit)}, idle={_idle.Count})".LogDebug();

            reg.Running.Disposable = reg.Worker.DoWork(work)
                .Do(r =>
                {
                    $"PullFanout.assign: {reg.Worker.Name} emitted {typeof(TR).Name} - publishing".LogDebug();
                    _publish(r);
                })
                // Completion, error and unsubscribe all mean the same thing to the pump: this worker
                // is no longer running that item. A per-item failure must never cost the fleet a
                // worker, which is what a read loop that terminated on a throw used to do.
                .Finally(() => OnPump(() => Finished(reg)))
                .Subscribe(
                    _ => { },
                    e => $"PullFanout.assign: {reg.Worker.Name} errored on {typeof(T).Name}: {e.GetType().Name}: {e.Message}".LogDebug());
        }

        private void Finished(Reg reg)
        {
            Bump(_running, reg.Unit, -1);
            reg.Connection.IsReserved = false;

            if (reg.Cancelled) return;

            $"PullFanout.assign: {reg.Worker.Name} finished - available again".LogDebug();
            MakeIdle(reg);
            Drain();
        }

        private void MakeIdle(Reg reg)
        {
            if (reg.Cancelled || reg.Node != null) return;

            reg.Node = _idle.AddLast(reg);
            _parked.OnNext(reg.Worker.Name);
        }

        private void Unregister(Reg reg)
        {
            reg.Cancelled = true;
            if (reg.Node != null)
            {
                _idle.Remove(reg.Node);
                reg.Node = null;
            }

            Workers.Remove(reg.Worker.Name);
            $"PullFanout.RegisterWorker.Dispose: removing {reg.Worker.Name} pool={Workers.Count}".LogDebug();
        }

        /// <summary>
        /// Put a decision on the pump, unless there is no longer a pump to put it on.
        ///
        /// <para>Teardown order is not ours to choose. A container unwinding its graph, or a test
        /// holding both the fanout and its registrations in one CompositeDisposable, can dispose
        /// this before the worker subscriptions it handed out - and a worker unregistering itself
        /// afterwards must be a no-op, not an ObjectDisposedException thrown out of somebody else's
        /// Dispose.</para>
        /// </summary>
        private void OnPump(Action decide)
        {
            if (_disposed) return;

            try
            {
                _pump.OnNext(decide);
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private static int Count(Dictionary<string, int> of, string unit) =>
            of.TryGetValue(unit, out var n) ? n : 0;

        private static void Bump(Dictionary<string, int> of, string unit, int by)
        {
            var next = Count(of, unit) + by;
            of[unit] = next < 0 ? 0 : next;
        }

        public void Dispose()
        {
            _disposed = true;
            _resources.Dispose();
        }

        /// <summary>One registered worker, as the pump sees it.</summary>
        private sealed class Reg
        {
            public IClusterWorker<T, TR> Worker;
            public WorkerConnection<T, TR> Connection;
            public string[] Keys;

            /// <summary>What this worker counts as for fairness - its machine, or itself.</summary>
            public string Unit;

            /// <summary>Its place in the idle list, or null while it is running something.</summary>
            public LinkedListNode<Reg> Node;

            /// <summary>The item it is running, so unregistering stops it.</summary>
            public readonly SerialDisposable Running = new SerialDisposable();

            public bool Cancelled;
        }
    }
}
