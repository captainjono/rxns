using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using Rxns.Collections;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns.Cloud.Intelligence
{
    public abstract class WorkerFanoutWithOverflow<T, TR> : IClusterFanout<T, TR> where TR : IRxn
    {
        public IDictionary<string, WorkerConnection<T, TR>> Workers { get; private set; } = new UseConcurrentReliableOpsWhenCastToIDictionary<string, WorkerConnection<T, TR>>(new ConcurrentDictionary<string, WorkerConnection<T, TR>>());
        protected readonly ISubject<int> WorkerConnected = new BehaviorSubject<int>(0);

        protected readonly Stack<T> _overflow = new Stack<T>(0);
        protected Action<IRxn> _publish;


        public void Attach(Action<IRxn> workCompletedHandler)
        {
            _publish = workCompletedHandler;
        }

        public IDisposable RegisterWorker(IClusterWorker<T, TR> worker)
        {
            Workers.Add(worker.Name, new WorkerConnection<T, TR>(){Worker = worker});
            WorkerConnected.OnNext(WorkerConnected.Value() + 1);
            $"Worker registered, pool size {Workers.Count}".LogDebug();

            return Disposable.Create(() =>
            {
                Workers.Remove(worker.Name);
            });
        }

        private IObservable<TR> DoWorkUntilDrained(T c, IClusterWorker<T, TR> freeWorker)
        {
            return freeWorker.DoWork(c).Do(r =>
                {
                    "Competing for overflow".LogDebug();
                    _publish(r);
                })
                .ObserveOn(CurrentThreadScheduler.Instance)
                .SelectMany(_ =>
                {
                    return DoWorkfromOverflowIf(freeWorker, DoWorkUntilDrained);
                });
        }

        public abstract void Fanout(T cfg);

        protected void AddToOverflow(T cfg)
        {
            _overflow.Push(cfg);
        }

        private IObservable<TR> DoWorkfromOverflowIf(in IClusterWorker<T, TR> freeWorker, Func<T, IClusterWorker<T, TR>, IObservable<TR>> worker)
        {
            if (_overflow.Count > 0)
                return worker(_overflow.Pop(), freeWorker);

            return Rxn.Empty<TR>();
        }
    }
}
