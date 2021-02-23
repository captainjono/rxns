using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Rxns.Logging;

namespace Rxns.Cloud
{
    public interface IClusterWorker<T, TR>
    {
        string Name { get; }
        string Route { get; }
        IObservable<TR> DoWork(T work);
    }

    public class ClusterFanOut<T, TR>
    {
        public readonly IDictionary<string, IClusterWorker<T, TR>> Workers = new Dictionary<string, IClusterWorker<T, TR>>();
        
        public IDisposable RegisterWorker(IClusterWorker<T, TR> worker)
        {
            Workers.Add(worker.Route, worker);
            $"Worker registered, pool size {Workers.Count}".LogDebug();
            return Disposable.Empty;
        }

        private int _lastWorkerIndex = 0;

        public IObservable<TR> Fanout(T cfg) //todo make generic
        {
            if (Workers.Count >= _lastWorkerIndex)
            {
                _lastWorkerIndex = 0;
            }
            else
            {
                _lastWorkerIndex++;
            }

            var nextAgent = Workers.Skip(_lastWorkerIndex).FirstOrDefault().Value;

            $"Sending work to {nextAgent.Name} @ {nextAgent.Route}".LogDebug();

            return nextAgent.DoWork(cfg);
        }
    }

}