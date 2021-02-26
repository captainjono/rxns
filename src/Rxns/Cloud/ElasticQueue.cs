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
    /// This queue implements a completeing consumer where the hosts computer resources can be monitored
    /// and more workers consequently spawned as a result in order to maximise the resource consumption on a host.
    ///
    /// Workers can be dynamically added to the queue in order to drain it at a sufficent pace. These workers may be un process, out of process, or in the cloud.
    /// </summary>
    public class ElasticQueue<T, TR> : ReportStatusService, IRxnPublisher<IRxn>, IRxnProcessor<WorkerDiscovered<T, TR>>, IRxnProcessor<T> where TR : IRxn
    {
        public readonly ISubject<T> _work = new ReplaySubject<T>(1000);
        public IClusterFanout<T, TR> Workflow { get; private set; }
        public IObservable<bool> IsWorking => _isWorking.DistinctUntilChanged();
        private readonly BehaviorSubject<bool> _isWorking = new BehaviorSubject<bool>(false);
        private Action<IRxn> _publish;


        public ElasticQueue(IClusterFanout<T, TR> fanoutStratergy = null)
        {
            Workflow = fanoutStratergy ?? new CompeteFanout<T, TR>();
        }

        public void Queue(T item) //this probably needs to return IObservle(workdone) instead ? sync good or bad? returns a resaultOf!?
        {
            Workflow.Fanout(item);
        }


        public IObservable<IRxn> Process(WorkerDiscovered<T, TR> @event)
        {
            return Rxn.Create<IRxn>(() =>
            {
                //was converting this to a sharding queue service because basically thats that the elastic queue idea was
                //need to startup a shard for worker that connects to fan out to them?
                //or should i instead just pull the logic out of the sharding queue into the fanout layer
                //like originally intended? have a base fanout class that is a rxqueue-fanout?
                //then the fanout occours by assigning a worker name to each item?
                //i really just want to implement competeing consumer

                //this isnt working
                //need to think clearly about an approach to fanning out
                //should i go back to the blocking collection?
                //fanout is the actual class that implements the sharing queue.. so maybe its the 
                //wrong construct? no such thing as a generic sharding queue that can do diff types of fanout?

                // StartQueue(w => w.Id == @event.Worker.Name);

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