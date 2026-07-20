using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Rxns.Cloud.Intelligence;
using Rxns.DDD.Commanding;
using Rxns.Interfaces;
using Rxns.Logging;
using Rxns.Scheduling;

namespace Rxns.Cloud
{
    public class KillWorker : ServiceCommand
    {
        public string Name { get; set; }

        public KillWorker(string name)
        {
            Name = name;
        }


        public KillWorker()
        {

        }
    }


    /// <summary>
    /// This queue implements a fanout stratergy to suite different circumstances.
    ///
    /// Workers can be dynamically added to the queue in order to drain it at a sufficent pace. These workers may be in process, out of process, or in the cloud.
    /// </summary>
    public class ElasticQueue<T, TR> : ReportStatusService, IRxnPublisher<IRxn>, IServiceCommandHandler<KillWorker>, IRxnProcessor<WorkerDisconnected>, IRxnProcessor<WorkerInfoUpdated>, IRxnProcessor<WorkerDiscovered<T, TR>> where TR : IRxn
    {
        public readonly ISubject<T> _work = new ReplaySubject<T>(1000);
        public IClusterFanout<T, TR> Workflow { get; private set; }
        public IObservable<bool> IsWorking => _isWorking.DistinctUntilChanged();
        protected readonly BehaviorSubject<bool> _isWorking = new BehaviorSubject<bool>(false); 
        protected Action<IRxn> _publish;
        protected IDictionary<string, IDisposable> _workerConnections = new ConcurrentDictionary<string, IDisposable>();
        
        //todo: implement health monitoring
        public ElasticQueue(IClusterFanout<T, TR> fanoutStratergy = null)
        {
            Workflow = fanoutStratergy ?? new CompeteFanout<T, TR>((w, ww) => !w.Worker.IsBusy.Value());
        }
        
        public void Queue(T item)
        {
            Workflow.Fanout(item);
        }

        public IObservable<IRxn> Process(WorkerDiscovered<T, TR> @event)
        {
            return Rxn.Create<IRxn>(() =>
            {
                _workerConnections.Add(@event.Worker.Name, Workflow.RegisterWorker(@event.Worker));
            });
        }


        public IObservable<IRxn> Process(WorkerInfoUpdated @event)
        {
            return Rxn.Create<IRxn>(() =>
            {
                if (_workerConnections.ContainsKey(@event.Name))
                {
                    Workflow.Workers[@event.Name].Worker.Update(@event.Info);
                }
            });
        }

        public IObservable<IRxn> Process(WorkerDisconnected @event)
        {
            return Rxn.Create<IRxn>(() =>
            {
                if(_workerConnections.ContainsKey(@event.Name))
                {
                    _workerConnections[@event.Name].Dispose();
                    _workerConnections.Remove(@event.Name);
                }
            });
        }

        public IObservable<IRxn> Process(T @event)
        {
            return Rxn.Create<IRxn>(() => Queue(@event));
        }

        public virtual void ConfigiurePublishFunc(Action<IRxn> publish)
        {
            _publish = publish;
            Workflow.ConfigiurePublishFunc(_publish);
        }
        
        public IObservable<CommandResult> Handle(KillWorker command)
        {
            return Rxn.Create(() =>
            {
                if (_workerConnections.Count == 0)
                {
                    return CommandResult.Success().AsResultOf(command);
                }

                if (command.Name.IsNullOrWhitespace())
                {
                    var toKill = _workerConnections.FirstOrDefault();
                    _workerConnections[toKill.Key].Dispose();
                    _workerConnections.Remove(toKill.Key);

                    return CommandResult.Success().AsResultOf(command);
                }

                if (_workerConnections.ContainsKey(command.Name))
                {
                    _workerConnections[command.Name].Dispose();
                    _workerConnections.Remove(command.Name);

                    return CommandResult.Success().AsResultOf(command);
                }
                

                return CommandResult.Failure($"No worker named '{command?.Name}' found").AsResultOf(command);
            });
        }

    }
}