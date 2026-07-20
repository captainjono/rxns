using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading.Tasks;
using Rxns.Cloud;
using Rxns.DDD.BoundedContext;
using Rxns.DDD.Commanding;
using Rxns.Interfaces;
using Rxns.Interfaces.Reliability;

namespace Rxns.Playback
{

    public class EsTask : ServiceCommand, IReactiveEvent
    {
        public string Tenant { get; set; }
        public DateTime Created { get; set; }

        public EsTask(string tenant)
        {
            Tenant = tenant;
            Created = DateTime.Now;
        }
    }

    public class EsTaskResult : ICommandResult, IRxn, IReactiveEvent
    {
        public string Tenant { get; private set; }
        public string InResponseTo { get; private set; }
        public CmdResult Outcome { get; private set; }
        public string Message { get; private set; }
        public int TotalEvents { get; private set; }
        public DateTime TaskCreated { get; private set; }
        public DateTime TaskStarted { get; private set; }
        public DateTime EventGenerationComplete { get; private set; }
        public DateTime TaskCompleted { get; private set; }

        public TimeSpan TimeToSync
        {
            get { return TaskCompleted - EventGenerationComplete; }
        }

        public TimeSpan TimeToGenerate
        {
            get { return EventGenerationComplete - TaskStarted; }
        }

        public TimeSpan TimeInQueue
        {
            get { return TaskStarted - TaskCreated; }
        }

        public TimeSpan TotalTime
        {
            get { return TimeToSync + TimeToGenerate + TimeInQueue; }
        }


        public EsTaskResult()
        {

        }

        public EsTaskResult(string tenant)
        {
            Tenant = tenant;
        }

        public EsTaskResult SetStatistics(int totalEvents, DateTime taskQueued, DateTime taskStarted, DateTime eventGenerationComplete, DateTime taskCompleted)
        {
            TotalEvents = totalEvents;
            TaskCreated = taskQueued;
            TaskStarted = taskStarted;
            EventGenerationComplete = eventGenerationComplete;
            TaskCompleted = taskCompleted;
            return this;
        }

        public static EsTaskResult Success(string tenant)
        {
            return new EsTaskResult(tenant)
            {
                Outcome = CmdResult.Success
            };
        }

        public static EsTaskResult Failure(string tenant, string message)
        {
            return new EsTaskResult(tenant)
            {
                Outcome = CmdResult.Failure,
                Message = message
            };
        }

        public EsTaskResult AsResultOf(EsTask cmd)
        {
            Tenant = cmd.Tenant;
            InResponseTo = cmd.Id;
            return this;
        }

        public CmdResult Result { get; }
    }

    public interface IEsRepo
    {
        IObservable<IDomainEvent> GetEvents(string tenant);
        IAggRoot GetById(string tenant, string id);
    }

    /// <summary>
    /// </summary>
    public class EventSourcingSyncBroker<T> : ShardingQueueProcessingService<EsTask>, IRxnCfg where T : IEsRepo
    {
        private readonly T _repo;
        private readonly Func<IDomainEvent, string> _eventToFileNameSelector;
        private readonly ICurrentTenantsService _tenants;
        private readonly ITapeArrayFactory _recoder;
        private readonly IRxnTapeToTenantRepoPlaybackAutomator _tapeRepo;
        private readonly ITenantDiscardRepository _discards;
        private readonly IReliabilityManager _reliably;
        private readonly IFileSystemService _fs;

        public IObservable<IRxn> ConfigureInput(IObservable<IRxn> pipeline)
        {
            return pipeline;
        }

        public string Reactor
        {
            get { return "EventSync"; }
        }

        public IDeliveryScheme<IRxn> InputDeliveryScheme { get; private set; }

        public bool MonitorHealth
        {
            get { return false; }
        }

        public RxnMode Mode { get; }

        public EventSourcingSyncBroker(T repo,
                                    Func<IDomainEvent, string> eventToFileNameSelector,
                                    ICurrentTenantsService tenants,
                                    ITapeArrayFactory recoder,
                                    IRxnTapeToTenantRepoPlaybackAutomator tapeRepo,
                                    ITenantDiscardRepository discards,
                                    IReliabilityManager reliably,
                                    IFileSystemService fs,
                                    IScheduler queueScheduler = null
                                    ) 
            : base("EsTasks", true, queueScheduler) 
        {
            _repo = repo;
            _eventToFileNameSelector = eventToFileNameSelector;
            _tenants = tenants;
            _recoder = recoder;
            _tapeRepo = tapeRepo;
            _discards = discards;
            _reliably = reliably;
            _fs = fs;
        }

        public override IObservable<CommandResult> Start(string @from = null, string options = null)
        {
            return Rxn.DfrCreate(() =>
            {
                CreateOneShardForEach(_tenants.ActiveTenants, (t, es) => t.Equals(es.Tenant, StringComparison.OrdinalIgnoreCase));
                StartQueue();
                return CommandResult.Success();
            });
        }

        public string GetStagingDirForTenant(string tenant)
        {
            return @"c:\dump\debug1\{0}_sync".FormatWith(tenant);
        }

        protected override void StartQueue(params Func<EsTask, bool>[] workerShardSelector)
        {
            _shardQueue = new Subject<EsTask>();
            _queueFunc = (item) => _shardQueue.OnNext(item);

            OnInformation("Queue configured as {0}", _isSynchronous ? "sync" : "async");

            _shardQueue.ObserveOn(TaskPoolSchedulerWithLimiter.ToScheduler(4))
                .SelectMany(request => this.ReportExceptions(() => ProcessQueueItem(request).Where(e => e != null)))
                .Select(results =>
                {
                    CurrentThreadScheduler.Instance.Run(() => _publish(results));
                    return new Unit();
                })
                .Catch<Unit, Exception>(e =>
                {
                    OnError(e);
                    return new Unit().ToObservable();
                })
                .Subscribe(_ => { },
                    error => OnError("Terminating the consumer for this queue processor due to: {0}", error));
        }

        protected override IObservable<IRxn> ProcessQueueItem(EsTask task)
        {
            var taskStarted = DateTime.Now;
            var eventGenerationComplete = DateTime.Now;
            var kCounter = 0;
            var eventCount = 0;

            return Rxn.DfrCreate(() =>
            {
                OnVerbose("Starting sync of '{0}'", task.Tenant);
                var stagingDir = GetStagingDirForTenant(task.Tenant);
                var staging = _recoder.Create<IDomainEvent>(stagingDir, e => _eventToFileNameSelector(e));

                OnInformation("Loading existing tapes for '{0}'", task.Tenant);
                //maybe i shouldnt do this, because a previous sync may still be streaming event tapes, so we should leave those alone?
                //or should we have some kind of lock file, so only files which arnt locked get loaded again?
                staging.Load().WaitR(); //any existing existing tapes will be appended too
                

                OnInformation("Looking for latest events '{0}'", task.Tenant);

                return _repo.GetEvents(task.Tenant)
                    .Do(@event => staging.Record(@event))
                    .Do(_ =>
                    {
                        if (++eventCount != 10000) return;

                        kCounter++;
                        eventCount = 0;
                        OnVerbose("Found {0}0k events so far", kCounter);
                    })
                    .LastOrDefaultAsync()
                    .Select(_ =>
                    {
                        eventGenerationComplete = DateTime.Now;
                        staging.EjectAll();

                        //OnInformation("Committing '{0}' staged events to repositories for '{1}'", eventCount + (10000 * kCounter), task.Tenant);
                        //CommitStagedEventsToAggsFor(task.Tenant, stagingDir, staging, _tapeRepo);

                        OnInformation("Done extracting '{0}' events for '{1}'", eventCount + (10000 * kCounter), task.Tenant);

                        return EsTaskResult.Success(task.Tenant);
                    });
            })
            .Catch<EsTaskResult, Exception>(e => EsTaskResult.Failure(task.Tenant, e.ToString()).ToObservable())
            .Select(result =>
            {
                result = result.SetStatistics(kCounter * 10000 + eventCount, task.Created, taskStarted, eventGenerationComplete, DateTime.Now);

                if (result.Outcome == CmdResult.Success)
                    OnInformation("Successfuly synced tenant '{0}' in '{1}'", result.Tenant, result.TotalTime);
                else
                    OnError("Sync of tenant '{0}' failed in '{1}' with '{2}'", result.Tenant, result.TotalTime, result.Message);

                return result.AsResultOf(task);
            });
        }

        private void CommitStagedEventsToAggsFor(string tenant, string stagingDir, ITapeRepository staging, IRxnTapeToTenantRepoPlaybackAutomator tapeRepo)
        {
            //pooled memory to reduce allocations
            tapeRepo.Play(
                stagingDir,
                tape =>
                {

                    var tenantstr = "{0}_sync\\".FormatWith(tenant);
                    var offSSet = tape.IndexOf(tenantstr) + tenantstr.Length;
                    var repo = tape.Substring(offSSet, tape.Length - offSSet);

                    return _repo.GetById(tenant, IdFromFileName(repo));
                },
                (agg, events) =>
                {

                },
                staging,
                _fs,
                (tape, repo, e) =>
                {
                    //should we use a retryBucket as well? on the next sync around its going to pickup the file again though...
                    OnError(e);
                    var fileTape = tape.Source as IFileTapeSource;
                    if (fileTape != null)
                    {
                        OnInformation("Discarding '{0}'", IdFromFileName(fileTape.File.Name));
                        _discards.DiscardPoisonTape(tenant, fileTape, e).WaitR();
                    }
                });
        }

        public string IdFromFileName(string id)
        {
            return id;//.FromBase64AsString();
        }

    }
}
