using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
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
        private readonly IDictionary<string, IClusterWorker<T, TR>> _agents = new Dictionary<string, IClusterWorker<T, TR>>();
        
        public IDisposable RegisterWorker(IClusterWorker<T, TR> worker)
        {
            _agents.Add(worker.Route, worker);
            $"Worker registered, pool size {_agents.Count}".LogDebug();
            return Disposable.Empty;
        }

        private int _lastWorkerIndex = 0;

        public IObservable<TR> Fanout(T cfg) //todo make generic
        {
            if (_agents.Count >= _lastWorkerIndex)
            {
                _lastWorkerIndex = 0;
            }
            else
            {
                _lastWorkerIndex++;
            }

            var nextAgent = _agents.Skip(_lastWorkerIndex).FirstOrDefault().Value;

            $"Sending work to {nextAgent.Name} @ {nextAgent.Route}".LogDebug();

            return nextAgent.DoWork(cfg);
        }
    }

}