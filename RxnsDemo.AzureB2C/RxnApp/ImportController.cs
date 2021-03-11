using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Rxns.DDD.CQRS;
using Rxns.NewtonsoftJson;
using Rxns.WebApiNET5.NET5WebApiAdapters.RxnsApiAdapters;
using RxnsDemo.AzureB2C.Rxns;
using RxnsDemo.AzureB2C.Rxns.Tenant;

namespace RxnsDemo.AzureB2C.RxnApps
{
    //[Authorize(Roles = RxnClaimTypesEx.Role_UserImport)]
    public class ImportController : DomainCommandApiController
    {
        private readonly ImportDomainApi _importApi;
        private readonly IDomainCommandMediator _commandMediator;
        private readonly IDomainQueryMediator _queryMediator;
        private readonly IAppControllerToUrlLinks _linkProvider;

        public ImportController(ImportDomainApi importApi, IAppControllerToUrlLinks linkProvider)
        {
            _importApi = importApi;
            _linkProvider = linkProvider;
        }

        [Route("{tenant}/import/users")]
        [HttpPost]
        public async Task<IActionResult> StartImportingUsers(string tenant, [FromBody]UserCreatedEvent[] import)
        {
            try
            {
                var importId = await _importApi.StartImportingUsers(tenant, User.Identity.Name, import);

                return Accepted(importId, _linkProvider.CreateLinkFor(this, "importProgress", new { tenant = tenant, importId = importId }));
            }
            catch (DomainCommandException dc)
            {
                return BadRequest(dc);
            }
            catch (Exception e)
            {
                OnError(e);
                return InternalServerError(e);
            }
        }

        [Route("{tenant}/progress/{importId}", Name = "importProgress")]
        [HttpGet]
        public async Task<IActionResult> ImportProgress(string tenant, string importId)
        {
            try
            {
                return Ok(await _importApi.ImportProgress(tenant, importId));
            }
            catch (DomainQueryException e)
            {
                return BadRequest(e);
            }
            catch (Exception e)
            {
                return InternalServerError(e);
            }
        }
    }

    public class RxnClaimTypesEx
    {
        public const string Role_UserImport = "RoleSecUserImport";
    }
    public static class RxnAzureExt
    {
        public async static Task<T> FromJson<T>(this HttpRequest req)
        {
            var c = await new StreamReader(req.Body).ReadToEndAsync();
            return c.FromJson<T>();
        }
    }
}
