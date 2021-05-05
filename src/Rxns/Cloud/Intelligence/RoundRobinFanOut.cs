using System.Linq;
using System.Reactive.Linq;
using Rxns.Cloud.Intelligence;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns.Cloud
{
    public class RoundRobinFanOut<T, TR> : WorkerFanoutWithOverflow<T, TR> where TR : IRxn
    {
        private int _lastWorkerIndex = 0;

        public override void Fanout(T cfg) //todo make generic
        {
            if (Workers.Count < 1)
            {
                AddToOverflow(cfg);
                return;
            }
            
            var nextAgent = Workers.Skip(_lastWorkerIndex++ % Workers.Count).FirstOrDefault().Value;

            //todo: properly capture the worker lifecycle so we can monitor it and optimise workloads
            $"Sending work to {nextAgent.Worker.Name} @ {nextAgent.Worker.Route}".LogDebug();

            nextAgent.DoWork = nextAgent.Worker.DoWork(cfg).Do(r => _publish(r)).Until();
        }
    }
}