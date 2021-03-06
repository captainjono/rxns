﻿using System;
using System.Data;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Web.Http;
using Rxns.WebApi;


namespace Rxns.Hosting.Updates
{
    //[Authorize]
    [RoutePrefix("updates")]
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

        [ValidateMimeMultipartContentFilter]
        [Route("{systemName}/{version}")]
        [HttpPost]
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

        [Route("{systemName}/{version}")]
        [HttpGet]
        public async Task<HttpResponseMessage> GetUpdate(string systemName, string version)
        {
            return await _updateManager.GetUpdate(systemName, version.IsNullOrWhiteSpace("Latest"))
                .Select(update =>
                {
                    var versionResponse = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StreamContent(update)
                    };

                    var fileName = String.Format("{0}-{1}.zip", systemName, version);
                    versionResponse.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
                    versionResponse.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue(fileName)
                    {
                        FileName = fileName
                    };

                    return versionResponse;
                })
                .Catch<HttpResponseMessage, FileNotFoundException>(e =>
                {
                    return new HttpResponseMessage(HttpStatusCode.NotFound)
                    {
                        Content = new StringContent(e.Message)
                    }.ToObservable();
                })
                .Catch<HttpResponseMessage, Exception>(e =>
                {
                    OnError(e);

                    return new HttpResponseMessage(HttpStatusCode.InternalServerError).ToObservable();
                });
        }

        [Route("{systemName}/{version}/get")]
        [HttpPost]
        public async Task<HttpResponseMessage> GetUpdateWithPost(string systemName, string version)
        {
            return await GetUpdate(systemName, version);
        }

        [Route("{systemName}/latest")]
        [HttpGet]
        public async Task<HttpResponseMessage> GetLatestUpdate(string systemName)
        {
            return await GetUpdate(systemName, null);
        }
        [Route("{systemName}/list")]
        [HttpGet]
        public async Task<IHttpActionResult> AllUpdates(string systemName = null, int top = 3)
        {
            try
            {
                if (systemName.IsNullOrWhiteSpace("all").Equals("all", StringComparison.OrdinalIgnoreCase))
                    systemName = null;

                var updates = await _updateManager.AllUpdates(systemName, top);

                return Ok(updates);

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

        [Route("{systemName}/{version}/push")]
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
