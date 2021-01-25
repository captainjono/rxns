using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Web.Http;
using Microsoft.Owin;
using Newtonsoft.Json.Linq;
using Rxns.DDD.Commanding;
using Rxns.Health.AppStatus;
using Rxns.Hosting.Updates;
using Rxns.Interfaces;
using Rxns.Logging;
using Rxns.Metrics;


namespace Rxns.WebApi.AppStatus
{
    //[Authorize]
    public class SystemStatusController : ReportsStatusApiControllerWithUpload
    {
        private readonly IAppStatusManager _appStatus;

        public SystemStatusController(IFileUploadProvider uploadProvider, IAppStatusManager appStatus)
            : base(uploadProvider)
        {
            _appStatus = appStatus;
        }

        [Route("systemstatus/heartbeats")]
        [HttpGet]
        public dynamic GetSystemStatus()
        {
            return _appStatus.GetSystemStatus();
        }
        

        [Route("systemstatus/heartbeats/publish")]
        [HttpPost]
        public void UpdateSystemStatus([FromBody] SystemStatusEvent status)
        {
            this.TryCatch(() =>
            {
                OnInformation("Received status from '{0}\\{1}'", status.Tenant, status.SystemName);

                status.IpAddress = ((OwinContext)Request.Properties["MS_OwinContext"]).Request.RemoteIpAddress;

                _appStatus.UpdateSystemStatus(status);
            });
        }

        [Route("systemstatus/heartbeat-2/publish")]
        [HttpPost]
        public async Task<RxnQuestion[]> UpdateSystemStatusWithMeta([FromBody] JObject statusJson)
        {
            try
            {
                var clientIp = ClientIpAddress();

                if (statusJson?["Status"] == null)
                {
                    OnWarning("Unknown status received from {0}: {1}", clientIp, statusJson.ToString());
                    return new RxnQuestion[] { };
                }

                var status = statusJson["Status"].ToObject<SystemStatusEvent>();
                var meta = statusJson["Meta"].ToObject<JToken[]>();
                var appRoute = status.GetRoute();

                status.IpAddress = clientIp;

                return await _appStatus.UpdateSystemStatusWithMeta(appRoute, status, meta);
            }
            catch (Exception e)
            {
                return new RxnQuestion[] {};
            }
        }

        [Route("systemstatus/log")]
        [HttpGet]
        public dynamic GetSystemLog()
        {
            return _appStatus.GetSystemLog();
        }

        [ValidateMimeMultipartContentFilter]
        [Route("systemstatus/logs/{tenantId}/{systemName}/publish")]
        [HttpPost]
        public IHttpActionResult Upload(string tenantId, string systemName)
        {
            try
            {
                //todo: fix wait issue
                GetUploadedFiles().Do(file =>
                {
                   _appStatus.UploadLogs(tenantId, systemName, file);

                }).Wait();
            }
            catch (ArgumentException) //occours when uploaded file is not .zip
            {
                return BadRequest("Only zip files can be supplied as logs");
            }
            catch (DuplicateNameException e) //occours when a duplicate update is uploaded
            {
                return BadRequest(e.Message);
            }
            catch (Exception e)
            {
                OnError(e);
                return InternalServerError();
            }

            return Ok();
        }
    }
}
