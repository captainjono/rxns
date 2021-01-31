using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Web.Http;
using Newtonsoft.Json.Linq;
using Rxns.Collections;
using Rxns.DDD.Commanding;
using Rxns.Health;
using Rxns.Health.AppStatus;
using Rxns.Interfaces;
using Rxns.Logging;
using Rxns.Metrics;

namespace Rxns.WebApi.MsWebApiAdapters.RxnsApiAdapters
{
    //[Authorize]
    [RoutePrefix("errors")]
    public class ErrorsController : ReportsStatusApiController
    {
        private readonly IAppErrorManager _errorMgr;

        public ErrorsController(IAppErrorManager errorMgr)
        {
            _errorMgr = errorMgr;
        }

        //[HideUncaughtExceptions]

        [Route("archive")]
        [HttpGet]
        public IEnumerable<SystemErrors> GetAllErrors(int page = 0, int size = 10, string tenant = null)
        {
            return _errorMgr.GetAllErrors(page, size, tenant);
        }

        //[HideUncaughtExceptions]
        [Route("")]
        [HttpGet]
        public IEnumerable<SystemErrors> GetOutstandingErrors(int page = 0, int size = 10, string tenant = null, string systemName = null)
        {
            return _errorMgr.GetOutstandingErrors(page, size, tenant, systemName);
        }

        [Route("{errorId}/meta")]
        [HttpGet]
        public IEnumerable<SystemLogMeta> GetErrorMeta(string errorId)
        {
            return _errorMgr.GetErrorMeta(errorId).ToEnumerable();
        }

        [Route("{errorId}/meta/publish")]
        [HttpPost]
        public void InsertErrorMeta(string errorId, [FromBody] SystemLogMeta[] meta)
        {
            _errorMgr.InsertErrorMeta(errorId, meta);
        }
        
        [Route("basicReport/publish")]
        [HttpPost]
        public void InsertError([FromBody] BasicErrorReport error)
        {
            _errorMgr.InsertError(error);
        }
    }
}
