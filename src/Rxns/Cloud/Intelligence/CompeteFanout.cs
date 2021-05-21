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

        private readonly Stack<T> _overflow = new Stack<T>(0);
        private Action<IRxn> _publish;
        
        public CompeteFanout(Func<WorkerConnection<T, TR>, T, bool> shouldFanOutToWorker)
        {
            _shouldFanOutToWorker = shouldFanOutToWorker;
        }

        public void Attach(Action<IRxn> workCompletedHandler)
        {
            _publish = workCompletedHandler;
        }

        public IDisposable RegisterWorker(IClusterWorker<T, TR> worker)
        {
            Workers.Add(worker.Name, new WorkerConnection<T, TR>()
            {
                Worker = worker
            });
            WorkerConnected.OnNext(WorkerConnected.Value() + 1);

            $"Worker registered, pool size {Workers.Count}".LogDebug();

            if (_overflow.Any())
            {
                DoWorkUntilDrained(_overflow.Pop(), worker).Until(); //todo: work out how to deal with this resource
            }

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

        public void Fanout(T work) //todo make generic
        {
            var freeWorker = Workers.Values.FirstOrDefault(w => _shouldFanOutToWorker(w, work));
            
            if (freeWorker != null)
            {
                $"Sending work to {freeWorker.Worker.Name} @ {freeWorker.Worker.Route}".LogDebug();

                freeWorker.DoWork = DoWorkUntilDrained(work, freeWorker.Worker).Until();
            }
            else
            {
                "Adding work to overflow, all workers busy".LogDebug();
                AddToOverflow(work);
            }
        }

        private void AddToOverflow(T cfg)
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
