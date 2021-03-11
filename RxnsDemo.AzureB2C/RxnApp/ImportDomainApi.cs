using System.Reactive.Threading.Tasks;
using System.Threading.Tasks;
using Rxns.DDD.Commanding;
using Rxns.DDD.CQRS;
using Rxns.Logging;
using RxnsDemo.AzureB2C.RxnApps.Events;
using RxnsDemo.AzureB2C.Rxns.Tenant;

namespace RxnsDemo.AzureB2C.RxnApps
{
    public class ImportDomainApi : ReportsStatus
    {
        private IDomainCommandMediator _commandMediator;
        private IDomainQueryMediator _queryMediator;

        public ImportDomainApi(IDomainCommandMediator commandMediator, IDomainQueryMediator queryMediator)
        {
            _commandMediator = commandMediator;
            _queryMediator = queryMediator;
        }

        public async Task<string> StartImportingUsers(string tenant, string userName, UserCreatedEvent[] import)
        {
            OnVerbose($"{userName} is importing users into {tenant}");

            var cmd = new StartImportOfUsersIntoTenantCmd(tenant, userName, import);
            var cmdResult = await _commandMediator.SendAsync<DomainCommandResult<string>>(cmd).ToTask();
            cmdResult.ThrowExceptions();

            OnInformation("Successfully queued import of users into '{0}' using data source '{1}'", tenant, import);

            return cmdResult.Result;
        }

        public Task<ProgressOfImport> ImportProgress(string tenant, string importId)
        {
            var msg = new ProgressOfUserImportIntoTenantQry(tenant, importId);
            return _queryMediator.SendAsync(msg).ToTask();
        }
    }

}
