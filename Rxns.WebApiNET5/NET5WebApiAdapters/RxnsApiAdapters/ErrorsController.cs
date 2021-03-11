using System.Collections.Generic;
using System.Reactive.Linq;
using Microsoft.AspNetCore.Mvc;
using Rxns.Health.AppStatus;
using Rxns.Logging;
using Rxns.Metrics;
using Rxns.NewtonsoftJson;

namespace Rxns.WebApiNET5.NET5WebApiAdapters.RxnsApiAdapters
{
    //[Authorize]
    public class ErrorsController : ReportsStatusApiController
    {
        private readonly IAppErrorManager _errorMgr;

        public ErrorsController(IAppErrorManager errorMgr)
        {
            _errorMgr = errorMgr;
        }

        //[HideUncaughtExceptions]

        [Route("errors/archive")]
        [HttpGet]
        public IEnumerable<SystemErrors> GetAllErrors(int page = 0, int size = 10, string tenant = null)
        {
            return _errorMgr.GetAllErrors(page, size, tenant);
        }

        //[HideUncaughtExceptions]
        [Route("errors")]
        [HttpGet]
        public IEnumerable<SystemErrors> GetOutstandingErrors(int page = 0, int size = 10, string tenant = null, string systemName = null)
        {
            return _errorMgr.GetOutstandingErrors(page, size, tenant, systemName);
        }

        [Route("errors/{errorId}/meta")]
        [HttpGet]
        public IEnumerable<SystemLogMeta> GetErrorMeta(string errorId)
        {
            return _errorMgr.GetErrorMeta(errorId).ToEnumerable();
        }

        [Route("errors/{errorId}/meta/publish")]
        [HttpPost]
        public void InsertErrorMeta(string errorId, [FromBody] SystemLogMeta[] meta)
        {
            _errorMgr.InsertErrorMeta(errorId, meta);
        }
        
        [Route("errors/basicReport/publish")]
        [HttpPost]
        public void InsertError([FromBody] BasicErrorReport error)
        {
            //error.ToJson().LogDebug("SDADSDA");
            _errorMgr.InsertError(error);
        }
    }
}
