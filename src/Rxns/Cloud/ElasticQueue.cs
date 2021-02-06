using System;
using System.Collections.Generic;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Rxns.DDD.Commanding;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns.Cloud
{
    public interface IQueueWorker
    {
        string Name { get; set; }
        IObservable<CommandResult> DoWork(object work);
    }

    public class WorkerDiscovered<T, TR> : IRxn
    {
        public IClusterWorker<T, TR> Worker { get; set; }
    }
    
    /// <summary>
    /// This queue implements a completeing consumer where the hosts computer resources can be monitored
    /// and more workers consequently spawned as a result in order to maximise the resource consumption on a host.
    ///
    /// Workers can be dynamically added to the queue in order to drain it at a sufficent pace. These workers may be un process, out of process, or in the cloud.
    /// </summary>
    public class ElasticQueue<T, TR> : ReportsStatus, IRxnProcessor<WorkerDiscovered<T, TR>>
        where TR : IRxn
     {
        public readonly Subject<T> _work = new Subject<T>();
        public ClusterFanOut<T, TR> Workflow = new ClusterFanOut<T, TR>();
        private Action<IRxn> _publish;

        public ElasticQueue()
        {
            _work.SelectMany(work =>
            {
                $"Fanning out {work.GetType()}".LogDebug();
                
                return Workflow.Fanout(work);
            })
            .Do(r => _publish(r))
            .Catch<TR, Exception>(e =>
            {
                $"Worker failed with {e}".LogDebug();
                OnError(e);
                
                return Rxn.Empty<TR>();
            })
            .Until(e =>
            {
                "Fanout failed! Application is unstable".LogDebug();
                OnError(e);
            });
        }

        public void Queue(T item) //this probably needs to return IObservle(workdone) instead ? sync good or bad? returns a resaultOf!?
        {
            _work.OnNext(item);
        }

        public IObservable<IRxn> Process(WorkerDiscovered<T, TR> @event)
        {
            return Rxn.Create(() =>
            {
                Workflow.RegisterWorker(@event.Worker);

                return Rxn.Empty<IRxn>();
            });
        }

        public void ConfigiurePublishFunc(Action<IRxn> publish)
        {
            _publish = publish;
        }
     }
}