using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Rxns.Collections;
using Rxns.CQRS;
using Rxns.Interfaces;
using Rxns.Logging;
using Rxns.Metrics;

namespace Rxns.Health.AppStatus
{
    /// <summary>
    /// Represents an error occuring with a tenant
    /// </summary>
    public class TenantError : IRequireTenantContext, IRxn
    {
        public string Error { get; set; }
        public Guid Id { get; set; }
        public string Tenant { get; set; }
        public DateTime Timestamp { get; set; }
        public string System { get; set; }
        public string LogId { get; set; }

        public TenantError()
        {
            Id = Guid.NewGuid();
        }
        public bool HasTenantSpecified()
        {
            return Tenant.IsNullOrWhitespace();
        }

        public void AssignTenant(string tenant)
        {
            Tenant = tenant;
        }

        public void ForTenant(string tenant)
        {
            Tenant = tenant;
        }

    }

    public class LocalAppErrorManager : ReportsStatus, IRxnPublisher<IRxn>, IAppErrorManager
    {
        private readonly IErrorRepository _errorsRepo;
        private readonly IRxnManager<IRxn> _eventManager;
        private Action<IRxn> _publish;

        public LocalAppErrorManager(IErrorRepository errorsRepo, IRxnManager<IRxn> eventManager)
        {
            _eventManager = eventManager;
            _errorsRepo = errorsRepo;
        }


        public IObservable<SystemErrors> GetAllErrors(int page = 0, int size = 10, string tenant = null)
        {
            try
            {
                OnInformation("Getting all errors: page='{0}', size='{1}'", page, size);
                return _errorsRepo.GetErrors(tenant == null ? null : new Func<SystemErrors, bool>(w => w.Tenant == tenant), new Page() { Number = page, Size = size });
            }
            catch (Exception e)
            {
                OnWarning(e.ToString());
                return Rxn.Empty<SystemErrors>();
            }
        }

        public IObservable<SystemErrors> GetOutstandingErrors(int page = 0, int size = 10, string tenant = null, string systemName = null)
        {
            try
            {
                OnInformation("Getting outstanding errors: page='{0}', size={1}'", page, size);

                return _errorsRepo.GetErrors(new Func<SystemErrors, bool>(w => w.Actioned == false && w.Tenant == (tenant ?? w.Tenant) && w.System == (systemName ?? w.System)), new Page() { Number = page, Size = size });
            }
            catch (Exception e)
            {
                OnWarning(e.ToString());
                return Rxn.Empty<SystemErrors>();
            }
        }

        public IObservable<SystemLogMeta> GetErrorMeta(string errorId)
        {
            try
            {
                OnInformation("Getting error meta for errorid '{0}'", errorId);
                return _errorsRepo.GetErrorMeta(errorId);
            }
            catch (Exception e)
            {
                OnWarning(e.ToString());
                return Rxn.Empty<SystemLogMeta>();
            }
        }

        public void InsertErrorMeta(string errorReportId, SystemLogMeta[] meta)
        {
            try
            {

                if (meta.Length == 0)
                {
                    OnInformation("Inserting error meta has no data to process");
                    return;
                }

                OnInformation("Inserting meta for errodId '{0}'", errorReportId);
                _errorsRepo.AddErrorMeta(errorReportId,  meta);
            }
            catch (Exception e)
            {
                OnWarning(e.ToString());
            }
        }

        public void InsertError(BasicErrorReport error)
        {
            try
            {
                OnInformation("Inserting error report '{0}'", error.Error.Substring(0, error.Error.Length > 80 ? 80 : error.Error.Length));

                var errorId = _errorsRepo.AddError(error);

                _eventManager.Publish(new TenantError()
                {
                    Tenant = error.Tenant,
                    Error = error.Error,
                    System = error.System,
                    Timestamp = error.Timestamp,
                    LogId = errorId.ToString()
                });
            }
            catch (Exception e)
            {
                OnWarning(e.ToString());
            }
        }

        public void ConfigiurePublishFunc(Action<IRxn> publish)
        {
            _publish = publish;
        }
    }
}
