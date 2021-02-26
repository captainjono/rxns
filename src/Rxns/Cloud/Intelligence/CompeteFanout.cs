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
        public IDictionary<string, IClusterWorker<T, TR>> Workers { get; private set; } = new UseConcurrentReliableOpsWhenCastToIDictionary<string, IClusterWorker<T, TR>>(new ConcurrentDictionary<string, IClusterWorker<T, TR>>());
        private readonly ISubject<int> WorkerConnected = new BehaviorSubject<int>(0);

        private readonly Stack<T> _overflow = new Stack<T>(0);
        private Action<IRxn> _publish;


        public void Attach(Action<IRxn> workCompletedHandler)
        {
            _publish = workCompletedHandler;
        }

        public IDisposable RegisterWorker(IClusterWorker<T, TR> worker)
        {
            Workers.Add(worker.Name, worker);
            WorkerConnected.OnNext(WorkerConnected.Value() + 1);
            $"Worker registered, pool size {Workers.Count}".LogDebug();


            return Disposable.Create(() =>
            {
                Workers.Remove(worker.Name);
            });
        }

        private void DoWorkUntilDrained(T c, IClusterWorker<T, TR> freeWorker)
        {
            freeWorker.DoWork(c).Do(r =>
            {
                "Competing for overflow".LogDebug();
                _publish(r);
                CurrentThreadScheduler.Instance.Run(() => DoWorkfromOverflowIf(freeWorker, DoWorkUntilDrained));
            }).Until();
        }

        public void Fanout(T cfg) //todo make generic
        {
            WorkerConnected.Where(w => w > 0).FirstAsync().SelectMany(_ => Rxn.Create(() =>
            {
                var freeWorker = Workers.FirstOrDefault(w => !w.Value.IsBusy.Value());

                if (freeWorker.Value != null)
                {
                    $"Sending work to {freeWorker.Value.Name} @ {freeWorker.Value.Route}".LogDebug();

                    DoWorkUntilDrained(cfg, freeWorker.Value);
                }
                else
                {
                    "Adding work to overflow, all workers busy".LogDebug();
                    AddToOverflow(cfg);
                }

                //todo: fix this fan out, should be decoupled from actual dowork

            })).Until();
        }

        private void AddToOverflow(T cfg)
        {
            _overflow.Push(cfg);
        }

        private void DoWorkfromOverflowIf(in IClusterWorker<T, TR> freeWorker, Action<T, IClusterWorker<T, TR>> worker)
        {
            if (_overflow.Count > 0)
                worker(_overflow.Pop(), freeWorker);
        }
    }
}
