using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Rxns.Cloud.Intelligence;
using Rxns.Health;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns.Cloud
{
    /// <summary>
    /// This queue implements a fanout stratergy to suite different circumstances.
    ///
    /// Workers can be dynamically added to the queue in order to drain it at a sufficent pace. These workers may be in process, out of process, or in the cloud.
    /// </summary>
    public class ElasticQueue<T, TR> : ReportStatusService, IRxnPublisher<IRxn>, IRxnProcessor<WorkerDiscovered<T, TR>> where TR : IRxn
    {
        public readonly ISubject<T> _work = new ReplaySubject<T>(1000);
        public IClusterFanout<T, TR> Workflow { get; private set; }
        public IObservable<bool> IsWorking => _isWorking.DistinctUntilChanged();
        protected readonly BehaviorSubject<bool> _isWorking = new BehaviorSubject<bool>(false); 
        protected Action<IRxn> _publish;
        
        //todo: implement health monitoring
        public ElasticQueue(IClusterFanout<T, TR> fanoutStratergy = null)
        {
            Workflow = fanoutStratergy ?? new CompeteFanout<T, TR>();
        }

        public void Queue(T item)
        {
            Workflow.Fanout(item);
        }

        public IObservable<IRxn> Process(WorkerDiscovered<T, TR> @event)
        {
            return Rxn.Create<IRxn>(() =>
            {
                Workflow.RegisterWorker(@event.Worker);
            });
        }
        
        public IObservable<IRxn> Process(T @event)
        {
            return Rxn.Create<IRxn>(() => Queue(@event));
        }

        public void ConfigiurePublishFunc(Action<IRxn> publish)
        {
            _publish = publish;
            Workflow.Attach(_publish);
        }
    }
}