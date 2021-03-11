using Rxns.Hosting;
using Rxns.Redis;
using RxnsDemo.AzureB2C.RxnApps.Events;
using RxnsDemo.AzureB2C.Rxns;
using RxnsDemo.AzureB2C.Rxns.Tenant;

namespace RxnsDemo.AzureB2C.RxnApps
{
    public class ImportUserModule : IAppModule
    {
        public IRxnLifecycle Load(IRxnLifecycle lifecycle)
        {
            var importCache = new RedisCacheFactory(new RedisCfg()
            {
                PartitionId = "imports",
                RedisConnectionString = "rxns.redis.cache.windows.net:6380,password=d5S3MSpyUw4PHAIQ2tkhcUWR5nZx+qhWyGWt0lGkBoE=,ssl=True,abortConnect=False"
            });

            var progressOfImportCache = importCache.Create<string, ProgressOfImport>("progressOfImports");

            return lifecycle
                //Domain services
                .CreatesOncePerApp(_ => new ImportProgressView(progressOfImportCache))
                .CreatesOncePerApp<ImportDataCommandsHandler>()
                //Domain API
                .RespondsToCmd<StartImportOfUsersIntoTenantCmd>()
                .RespondsToQry<ProgressOfUserImportIntoTenantQry>()
                //Domain Event API
                .Emits<UserCreatedEvent>()
                .Emits<ImportOfUserIntoTenantFailureEvent>()
                .Emits<ImportOfUserIntoTenantSuccessfulEvent>()
                .Emits<ImportFailureResult>()
                .Emits<UserImportSuccessResult>()
                .Emits<ImportOfUsersIntoTenantQueuedEvent>()
                .Emits<ImportOfUsersIntoTenantEvent>()
                .Emits<ImportOfUsersIntoTenantStartedEvent>()
                .Emits<ImportOfUsersIntoTenantStagedEvent>()

                //support 201 ACCEPTED
                .CreatesOncePerApp<AspnetCoreControllerLinkProvider>()
                ;

        }
    }
}
