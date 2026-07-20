using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Rxns.WebApiNET5.NET5WebApiAdapters;


namespace RxnsDemo.AzureB2C.RxnApps.RxnAppAzureFunc.ImportProgress
{
    public class ImportProgressFunc : ReportsStatusApiController
    {
        private ImportDomainApi _importServiceClient;

        public ImportProgressFunc(ImportDomainApi ic)
        {
            _importServiceClient = ic;
        }

        [FunctionName("ProgressOfUserImportIntoTenantQry")]
        public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequest req)
        {
            return Ok(await _importServiceClient.ImportProgress(req.Query["tenant"], req.Query["importId"]));
        }
    }

}