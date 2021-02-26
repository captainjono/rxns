using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Rxns.Cloud.Intelligence;
using Rxns.Collections;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns.Cloud
{
    public class RoundRobinFanOut<T, TR> : WorkerFanoutWithOverflow<T, TR> where TR : IRxn
    {
        private int _lastWorkerIndex = 0;

        public override void Fanout(T cfg) //todo make generic
        {
            var nextAgent = Workers.Skip(_lastWorkerIndex++ % Workers.Count).FirstOrDefault().Value;

            //todo: properly capture the worker lifecycle so we can monitor it and optimise workloads
            $"Sending work to {nextAgent.Name} @ {nextAgent.Route}".LogDebug();

            nextAgent.DoWork(cfg).Do(r => _publish(r)).Until();
        }
    }
}