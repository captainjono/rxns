using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Rxns;
using Rxns.DDD.BoundedContext;
using Rxns.DDD.CQRS;
using Rxns.Interfaces;
using RxnsDemo.AzureB2C.RxnApps.Events;

namespace RxnsDemo.AzureB2C.RxnApps
{
    public class ImportFailureResult : DomainEvent
    {
        public string Error { get; private set; }
        public string UserName { get; }

        public ImportFailureResult(string tenant, string userName, string error) : base(tenant)
        {
            Error = error;
            UserName = userName;
        }
    }

    public class UserImportSuccessResult : DomainEvent
    {
        public string UserName { get; private set; }

        public UserImportSuccessResult(string tenant, string userName) : base(tenant)
        {
            UserName = userName;
        }
    }

    /// <summary>
    /// realtime 
    /// </summary>
    public class ImportProgressView :   IDomainQueryHandler<ProgressOfUserImportIntoTenantQry, ProgressOfImport>,
                                        IRxnProcessor<ImportOfUsersIntoTenantEvent>,
                                        IRxnCfg
    {
        private readonly IDictionary<string, ProgressOfImport> _progressReports = null;

        public ImportProgressView(IDictionary<string, ProgressOfImport> progressReportCache = null)
        {
            _progressReports = progressReportCache ??
                               new ConcurrentDictionary<string, ProgressOfImport>(
                                   new ConcurrentDictionary<string, ProgressOfImport>());
        }

        public IObservable<ProgressOfImport> Handle(ProgressOfUserImportIntoTenantQry query)
        {
            return Rxn.Create(() =>
            {
                if (!_progressReports.ContainsKey(query.ImportId))
                        throw new DomainQueryException(query, $"No progress for Id '{query.ImportId}'. Verify the importId and try again soon if valid.");
                return _progressReports[query.ImportId];
            });
        }
        
        public IObservable<IRxn> Process(ImportOfUsersIntoTenantEvent @event)
        {
            return ((IObservable<IRxn>)Process((dynamic)@event)); //singlethread
        }

        public IObservable<IRxn> Process(ImportOfUsersIntoTenantQueuedEvent @event)
        {
            return Rxn.Create<IRxn>(() =>
            {
                _progressReports.Add(@event.ImportId, new ProgressOfImport(@event.Tenant, "Users"));
            });
        }

        public IObservable<IRxn> Process(ImportOfUsersIntoTenantStartedEvent @event)
        {
            return Rxn.Create<IRxn>(() =>
            {
                _progressReports[@event.ImportId].InProgress();
            });
        }

        public IObservable<IRxn> Process(ImportOfUserIntoTenantSuccessfulEvent @event)
        {
            return Rxn.Create<IRxn>(() =>
            {
                _progressReports[@event.ImportId].MarkAsSuccess(new UserImportSuccessResult(@event.Tenant, @event.UserName));
                
                if (_progressReports[@event.ImportId].IsComplete())
                {
                    _progressReports[@event.ImportId].Complete();
                }
            });
        }

        public IObservable<IRxn> Process(ImportOfUserIntoTenantFailureEvent @event)
        {
            return Rxn.Create<IRxn>(() =>
            {
                _progressReports[@event.ImportId].MarkAsFailure(new ImportFailureResult(@event.Tenant, @event.UserName,  @event.Error));

                if (_progressReports[@event.ImportId].IsComplete())
                {
                    _progressReports[@event.ImportId].Complete();
                }
            });
        }

        public IObservable<IRxn> Process(ImportOfUsersIntoTenantStagedEvent @event)
        {
            return Rxn.Create<IRxn>(() =>
            {
                _progressReports[@event.ImportId].Expect(@event.ResultCount);
            });
        }

        public string Reactor => "ImportUsers";
        public IObservable<IRxn> ConfigureInput(IObservable<IRxn> pipeline)
        {
            return pipeline;
        }

        public IDeliveryScheme<IRxn> InputDeliveryScheme { get; }
        public bool MonitorHealth { get; } = true;
        public RxnMode Mode { get; } = RxnMode.InProcess;
    }
}
