using System;
using System.Data;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using Rxns.DDD.Commanding;
using Rxns.Health.AppStatus;
using Rxns.Interfaces;

namespace Rxns.WebApiNET5.NET5WebApiAdapters.RxnsApiAdapters
{
    public class AppHeatbeat : IRxn
    {
        public SystemStatusEvent Status { get; set; }
        public object Meta { get; set; }
    }

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

                status.IpAddress = GetRequestIP();

                _appStatus.UpdateSystemStatus(status);
            });
        }

        [Route("systemstatus/heartbeat-2/publish")]
        [HttpPost]
        public async Task<RxnQuestion[]> UpdateSystemStatusWithMeta([FromBody] AppHeatbeat status)
        {
            try
            {
                var clientIp = ClientIpAddress();

                if (status?.Status == null)
                {
                    OnWarning("Unknown status received from {0}: {1}", clientIp, status);
                    return new RxnQuestion[] { };
                }

                
                var appRoute = status.Status.GetRoute();

                status.Status.IpAddress = clientIp;

                return await _appStatus.UpdateSystemStatusWithMeta(appRoute, status.Status, status.Meta);
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

        //[ValidateMimeMultipartContentFilter]
        [Route("systemstatus/logs/{tenantId}/{systemName}/publish")]
        [HttpPost]
        public IActionResult Upload(string tenantId, string systemName)
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
                return BadRequest();
            }

            return Ok();
        }
    }
}
