using System;
using System.Data;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Rxns.Hosting;
using Rxns.Hosting.Updates;

namespace Rxns.WebApiNET5.NET5WebApiAdapters.RxnsApiAdapters
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class DisableAspnetCoreModelBinding : Attribute, IResourceFilter
    {
        public void OnResourceExecuting(ResourceExecutingContext context)
        {
            var factories = context.ValueProviderFactories;

            factories.RemoveType<FormValueProviderFactory>();
            factories.RemoveType<FormFileValueProviderFactory>();
            factories.RemoveType<JQueryFormValueProviderFactory>();
        }

        public void OnResourceExecuted(ResourceExecutedContext context)
        {
        }
    }

    //[Authorize]
    public class UpdatesController : ReportsStatusApiControllerWithUpload
    {
        private readonly IAppUpdateManager _updateManager;
        private readonly IUpdateStorageClient _client;
        private readonly IAppStatusStore _cmdHub;

        public UpdatesController(IFileUploadProvider uploadProvider, IAppUpdateManager updateManager)
            : base(uploadProvider)
        {
            _updateManager = updateManager;
        }

        //[ValidateMimeMultipartContentFilter]
        [Route("updates/{systemName}/{version}")]
        [HttpPost]
        [DisableAspnetCoreModelBinding]
        public async Task<HttpResponseMessage> Upload(string systemName, string version)
        {
            return await GetUploadedFiles()
                .SelectMany(file => _updateManager.Upload(systemName, version, file))
                .Select(s => new HttpResponseMessage(HttpStatusCode.Accepted))
                .Catch<HttpResponseMessage, ArgumentException>(e => new HttpResponseMessage(HttpStatusCode.BadRequest) { ReasonPhrase = "Only zip files can be supplied as updates"}.ToObservable())
                .Catch<HttpResponseMessage, DuplicateNameException>(e => new HttpResponseMessage(HttpStatusCode.BadRequest) { ReasonPhrase = e.Message}.ToObservable())
                .Catch<HttpResponseMessage, Exception>(e =>
                {
                    OnError(e);
                    return new HttpResponseMessage(HttpStatusCode.InternalServerError).ToObservable();
                });
        }

        [Route("updates/{systemName}")]
        [HttpGet]
        public Task<IActionResult> GetUpdate(string systemName)
        {
            return GetUpdate(systemName, null);
        }

        [Route("updates/{systemName}/{version}")]
        [HttpGet]
        public async Task<IActionResult> GetUpdate(string systemName, string version)
        {
            return await _updateManager.GetUpdate(systemName, version.IsNullOrWhiteSpace("Latest"))
                .Select(update =>
                {
                    return File(update, "application/zip", String.Format("{0}-{1}.zip", systemName, version));
                })
                .Catch<IActionResult, FileNotFoundException>(e =>
                {
                    return NotFound(systemName).ToObservable();
                  
                })
                .Catch<IActionResult, Exception>(e =>
                {
                    OnError(e);

                    return NotFound(systemName).ToObservable();
                }).ToTask();
        }

        [Route("updates/{systemName}/{version}/get")]
        [HttpPost]
        public async Task<IActionResult> GetUpdateWithPost(string systemName, string version)
        {
            return await GetUpdate(systemName, version);
        }

        [Route("updates/{systemName}/latest")]
        [HttpGet]
        public async Task<IActionResult> GetLatestUpdate(string systemName)
        {
            return await GetUpdate(systemName, null);
        }
        [Route("updates/{systemName}/list")]
        [HttpGet]
        public async Task<IActionResult> AllUpdates(string systemName = null, int top = 3)
        {
            try
            {
                if (systemName.IsNullOrWhiteSpace("all").BasicallyEquals("all"))
                    systemName = null;

                var res = await _updateManager.AllUpdates(systemName, top).ToTask();//hack to handle empty sequence;

                return Ok(res);

            }
            catch (FileNotFoundException e)
            {
                return NotFound();
            }
            catch (Exception e)
            {
                OnError(e);

                return InternalServerError(e);
            }
        }

        [Route("updates/{systemName}/{version}/push")]
        [HttpPost]
        public async Task<HttpResponseMessage> PushUpdate(string systemName, string version, [FromBody] string[] tenants)
        {
            return await _updateManager.PushUpdate(systemName, version, User.Identity.Name, tenants)
                .Select(ok => ok
                    ? new HttpResponseMessage(HttpStatusCode.Accepted)
                    : new HttpResponseMessage(HttpStatusCode.BadRequest)
                    {
                        ReasonPhrase = "No tenants were specified. Send a string[] under a tenants object in the body to specify who the update will target."
                    });
        }
    }
}
