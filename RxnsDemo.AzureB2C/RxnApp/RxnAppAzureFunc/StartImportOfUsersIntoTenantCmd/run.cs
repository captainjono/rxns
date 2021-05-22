using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using Rxns.DDD.Tenant;
using Rxns.WebApiNET5.NET5WebApiAdapters;
using RxnsDemo.AzureB2C.RxnApps;

namespace AzurFunc
{
    public class StartImportOfUsersIntoTenantCmdAzureFunc : ReportsStatusApiController
    {
        private readonly ImportDomainApi _importServiceClient;

        public StartImportOfUsersIntoTenantCmdAzureFunc(ImportDomainApi ic)
        {
            _importServiceClient = ic;
        }

        [FunctionName("StartImportOfUsersIntoTenantCmd")]
        public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req, ILogger log)
        {
            var importId = await _importServiceClient.StartImportingUsers(req.Query["tenant"], "username", await req.FromJson<UserCreatedEvent[]>());
            return Accepted(importId, $"getLinkForProgress?{importId}");
        }
    }
}