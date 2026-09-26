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
                // A worker can be discovered more than once: it reconnects, its route flaps, or its
                // process restarts and announces itself again under the same name. None of that is
                // exceptional, but _workerConnections is a ConcurrentDictionary held as an
                // IDictionary, and IDictionary.Add throws when the key is already present.
                //
                // The throw did not stay local. This processor is subscribed once to a stream of
                // discoveries, so the error did not skip one event - it ended the subscription, and
                // every worker discovered after it was never registered at all. On a cold fleet the
                // machines that finish booting later sit on the far side of that error: they come
                // up, they heartbeat, they report themselves idle, and no work is ever placed on
                // them. A hundred-machine run lost half its fleet to this and reported nothing.
                //
                // So a re-sighting replaces the previous registration rather than colliding with it.
                // The old connection is disposed first, which withdraws the stale entry from the
                // fan-out's own table before the new one is added, leaving exactly one registration
                // per name whichever order the sightings arrive in.
                var name = @event.Worker.Name;

                if (_workerConnections.TryGetValue(name, out var previous))
                {
                    $"Worker {name} discovered again - replacing its previous registration".LogDebug();

                    try
                    {
                        previous.Dispose();
                    }
                    catch (Exception ex)
                    {
                        // A registration that cannot be torn down must not cost us the new one, nor
                        // the stream that carries every worker behind it.
                        $"Worker {name} previous registration would not dispose: {ex.Message}".LogDebug();
                    }

                    _workerConnections.Remove(name);
                }

                _workerConnections[name] = Workflow.RegisterWorker(@event.Worker);
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