using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Rxns;
using Rxns.DDD.Tenant;
using Rxns.NewtonsoftJson;
using Rxns.WebApiNET5.NET5WebApiAdapters;

namespace RxnsDemo.AzureB2C.RxnApps.RxnAppAzureFunc.ImportProgress
{
    public class ImportIntoDbFunc : ReportsStatusApiController
    {
        private readonly AzureB2CToLegacyDbProcessor _usersDbOrchestrator;

        public ImportIntoDbFunc(AzureB2CToLegacyDbProcessor ic)
        {
            _usersDbOrchestrator = ic;
        }

        [FunctionName("ProgressOfUserImportIntoTenantQry")]
        public async void Run(
            [QueueTrigger("start-import-users")] string c,
            [Queue("import-users-finished")] IAsyncCollector<string> output
            )
        {
            var cmd = c.FromJson<UserCreatedEvent>();

            _usersDbOrchestrator
                .Process(cmd)
                .SelectMany(r => output.AddAsync(r.ToJson().ResolveAs(r.GetType())) //add reuslts to queue for import view to process
                .ToObservable())
                .Until();
        }
    }

}


