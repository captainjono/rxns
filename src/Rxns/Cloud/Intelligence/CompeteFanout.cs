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
using Rxns.Scheduling;

namespace Rxns.Cloud.Intelligence
{
    public class CompeteFanout<T, TR> : IClusterFanout<T, TR> where TR : IRxn
    {
        private readonly Func<WorkerConnection<T, TR>, T, bool> _shouldFanOutToWorker;
        public IDictionary<string, WorkerConnection<T, TR>> Workers { get; private set; } = new UseConcurrentReliableOpsWhenCastToIDictionary<string, WorkerConnection<T, TR>>(new ConcurrentDictionary<string, WorkerConnection<T, TR>>());
        private readonly ISubject<int> WorkerConnected = new BehaviorSubject<int>(0);

        public readonly ConcurrentStack<T> Overflow = new ConcurrentStack<T>();
        private Action<IRxn> _publish;

        // Serialises the pick decision in Fanout / RegisterWorker so two
        // concurrent calls can't double-pick the same worker before its
        // IsReserved flag flips. Phase 7n4 race fix — production saw 2
        // dispatches 60ms apart both land on W1_1 because IsBusy hadn't
        // updated yet. The lock is held only across the iterate-and-mark
        // section, never around the actual DoWork chain.
        private readonly object _pickLock = new object();

        public CompeteFanout(Func<WorkerConnection<T, TR>, T, bool> shouldFanOutToWorker)
        {
            _shouldFanOutToWorker = shouldFanOutToWorker;
        }
        public void ConfigiurePublishFunc(Action<IRxn> publish)
        {
            _publish = publish;
        }

        public IDisposable RegisterWorker(IClusterWorker<T, TR> worker)
        {
            var connection = new WorkerConnection<T, TR>() { Worker = worker };
            Workers.Add(worker.Name, connection);
            WorkerConnected.OnNext(WorkerConnected.Value() + 1);

            $"CompeteFanout.RegisterWorker: {worker.Name} route='{worker.Route}' busy={worker.IsBusy.Value()} info=[{string.Join(",", (worker.Info ?? new Dictionary<string,string>()).Select(kv => kv.Key + "=" + kv.Value))}] pool={Workers.Count} overflow={Overflow.Count}".LogDebug();
            $"Worker registered, pool size {Workers.Count}".LogDebug();

            if (!Overflow.IsEmpty)
            {
                // Peek before popping so a worker that doesn't match the item's tags
                // doesn't consume a slot (e.g. stale RemoteTestWorker stealing from a
                // session-tagged local run).
                if (Overflow.TryPeek(out var peeked))
                {
                    var matches = _shouldFanOutToWorker(connection, peeked);
                    $"CompeteFanout.RegisterWorker: peek overflow for {worker.Name} matches={matches} (busy={worker.IsBusy.Value()})".LogDebug();
                    if (matches)
                    {
                        if (Overflow.TryPop(out var item))
                        {
                            if (_shouldFanOutToWorker(connection, item))
                            {
                                $"Worker {worker.Name} picking up overflow ({Overflow.Count} pending)".LogDebug();
                                // Mirror the Fanout reserve/release contract on the
                                // overflow-drain path — without this, a fresh
                                // Fanout call landing immediately after registration
                                // could double-pick this worker before the popped
                                // overflow item's DoWork starts.
                                connection.IsReserved = true;
                                DoWorkUntilDrained(item, worker)
                                    .Finally(() => connection.IsReserved = false)
                                    .Until();
                            }
                            else
                            {
                                $"CompeteFanout.RegisterWorker: post-pop predicate flipped — re-queueing overflow item".LogDebug();
                                Overflow.Push(item);
                            }
                        }
                    }
                }
            }

            return Disposable.Create(() =>
            {
                $"CompeteFanout.RegisterWorker.Dispose: removing {worker.Name} pool={Workers.Count - 1}".LogDebug();
                Workers.Remove(worker.Name);
            });
        }

        private IObservable<TR> DoWorkUntilDrained(T initialWork, IClusterWorker<T, TR> freeWorker)
        {
            return Rxn.Create<TR>(o =>
            {
                Func<IScheduler, T, IDisposable> step = null;
                step = (sched, work) =>
                {
                    $"CompeteFanout.step: invoking {freeWorker.Name}.DoWork for {typeof(T).Name} busy={freeWorker.IsBusy.Value()}".LogDebug();
                    return freeWorker.DoWork(work)
                        .Do(
                            r =>
                            {
                                $"CompeteFanout.step: {freeWorker.Name} emitted {typeof(TR).Name} — publishing".LogDebug();
                                _publish(r);
                            },
                            ex => $"CompeteFanout.step: {freeWorker.Name}.DoWork errored: {ex.GetType().Name}: {ex.Message}".LogDebug())
                        .LastOrDefaultAsync()
                        .Do(_ =>
                        {
                            $"CompeteFanout.step: {freeWorker.Name}.DoWork completed — competing for overflow (pending={Overflow.Count})".LogDebug();
                            T next;
                            if (Overflow.TryPop(out next))
                                sched.Schedule(next, step);
                            else
                                o.OnCompleted();
                        })
                        .Subscribe(
                            _ => { },
                            ex => { $"CompeteFanout.step: OnError on {freeWorker.Name}: {ex.GetType().Name}: {ex.Message}".LogDebug(); o.OnError(ex); });
                };
                return CurrentThreadScheduler.Instance.Schedule(initialWork, step);
            });
        }

        public void Fanout(T work)
        {
            // Pick + reserve under a lock so two concurrent Fanout calls
            // can't race to the same worker before IsReserved flips. The
            // lock is held only for the in-memory pick decision; the
            // DoWork chain runs outside it.
            WorkerConnection<T, TR> picked = null;
            lock (_pickLock)
            {
                foreach (var w in Workers.Values)
                {
                    if (w.IsReserved) continue;            // already taken this round
                    if (_shouldFanOutToWorker(w, work))
                    {
                        picked = w;
                        picked.IsReserved = true;          // synchronous mark
                        break;
                    }
                }
            }

            if (picked != null)
            {
                // Clear IsReserved when the DoWork chain terminates
                // (completion, error, or external unsubscribe). Finally
                // fires on all three paths, so the worker becomes
                // pickable again exactly once its prior dispatch is done.
                picked.DoWork = DoWorkUntilDrained(work, picked.Worker)
                    .Finally(() => picked.IsReserved = false)
                    .Until();
            }
            else
            {
                Overflow.Push(work);
                $"Added work to overflow ({Overflow.Count} pending), all {Workers.Count} workers busy or reserved".LogDebug();
            }
        }
    }
}
