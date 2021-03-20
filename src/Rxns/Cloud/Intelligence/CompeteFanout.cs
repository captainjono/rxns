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

            DoWorkfromOverflowIf(worker, DoWorkUntilDrained);

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
            var freeWorker = Workers.Values.FirstOrDefault(w => !w.IsBusy.Value());
            
            if (freeWorker != null)
            {
                $"Sending work to {freeWorker.Name} @ {freeWorker.Route}".LogDebug();

                DoWorkUntilDrained(cfg, freeWorker);
            }
            else
            {
                "Adding work to overflow, all workers busy".LogDebug();
                AddToOverflow(cfg);
            }
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
