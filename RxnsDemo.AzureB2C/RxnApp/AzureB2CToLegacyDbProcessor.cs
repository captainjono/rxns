using System;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using Microsoft.CSharp.RuntimeBinder;
using Rxns;
using Rxns.Collections;
using Rxns.DDD.BoundedContext;
using Rxns.DDD.Commanding;
using Rxns.Interfaces;
using Rxns.Interfaces.Reliability;
using Rxns.NewtonsoftJson;
using RxnsDemo.AzureB2C.Rxns;
using RxnsDemo.AzureB2C.Rxns.Tenant;

namespace RxnsDemo.AzureB2C.RxnApps
{
    public class AzureB2CToLegacyDbProcessor : ReportsStatusBatchingViewProcessor,
                                           IRxnProcessor<UserCreatedEvent>,
                                           IRxnCfg
    {
        private readonly IExecutionContextFactory _execFactory;
        private readonly RetryBucket<UserCreatedEvent> _retryBucket;
        private readonly IExpiringCache<string, object> _idCache;
        private static readonly IScheduler _singleThread = new EventLoopScheduler();

        public AzureB2CToLegacyDbProcessor(ITenantDatabaseFactory contextFactory, IExecutionContextFactory execFactory, IReliabilityManager reliably, IScheduler retryScheduler = null)
            //since this is an event processor, it get a new thread to begin with, maintain that same thread for the dbCall
            : base(contextFactory, reliably, RxnSchedulers.Immediate)
        {
            _execFactory = execFactory;
            _retryBucket = new RetryBucket<UserCreatedEvent>(e => { Process(e).WaitR(); },
                                                                (evt, e) => OnError(new Exception("Retrying failed for {0}".FormatWith(evt.GetType().Name), e)),
                                                                (evt, error) => MarkAsPoison(evt, error),
                                                                "AzureB2CLegacyUserDb",
                                                                retryScheduler: retryScheduler ?? RxnSchedulers.TaskPool,
                                                                retryIn: TimeSpan.FromMinutes(30));

            _idCache = ExpiringCache.CreateConcurrent<string, object>(TimeSpan.FromMinutes(20), retryScheduler);
        }

        private void MarkAsPoison(IDomainEvent @event, Exception error)
        {
            _execFactory.FromDomain(@event).Tenant.Value.DiscardContext.DiscardPoisonEvent(@event, error).Wait();
        }

        private readonly Action<IDomainEvent, Exception, RetryBucket<IDomainEvent>, IReportStatus> _retryIf = (@evt, e, retryBucket, reporter) =>
        {
            if (e is NullReferenceException || e is ArgumentNullException)
                reporter.OnError(new Exception("Bad event received: {0}".FormatWith(@evt.ToJson().Replace('{', '[').Replace('{', ']')), e));
            else if (e is RuntimeBinderException)
                reporter.OnError(new Exception("Event not handled yet: {0}".FormatWith(@evt.GetType()), e));
            else
            {
                //unexpected error, retry later
                reporter.OnError(new Exception("Processing failed for {0} -> {1}".FormatWith(evt.GetType().Name, e.Message), e));
                retryBucket.Add(@evt);
            }
        };
        public IObservable<IRxn> Process(UserCreatedEvent @event)
        {
            return Rxn.Create(() =>
            {
                var tenantFromEvent = _execFactory.FromDomain(@event).Tenant.Value;
                var userDbForTenant = tenantFromEvent.DatabaseContext.GetUsersContext(tenantFromEvent.DatabaseContext.GetContext(@event.Tenant));

                userDbForTenant.RegisterUser(@event);
                TimeSpan.FromSeconds(1).Then().WaitR();


                return new UserImportSuccessResult(@event.Tenant, @event.UserName);
            });
        }

        public EventLoopScheduler _backgroundThread = new EventLoopScheduler();
        public string Reactor => "ImportUsers";
        public IObservable<IRxn> ConfigureInput(IObservable<IRxn> pipeline)
        {
            return pipeline.ObserveOn(_backgroundThread).SubscribeOn(_backgroundThread);
        }

        public IDeliveryScheme<IRxn> InputDeliveryScheme { get; }
        public bool MonitorHealth { get; } = true;
        public RxnMode Mode { get; } = RxnMode.InProcess;
    }
  
}
