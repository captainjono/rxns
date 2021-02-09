using System.Collections.Generic;
using System.Reactive.Linq;
using Microsoft.AspNetCore.Mvc;
using Rxns.Health.AppStatus;
using Rxns.Logging;
using Rxns.Metrics;

namespace Rxns.WebApiNET5.NET5WebApiAdapters.RxnsApiAdapters
{
    //[Authorize]
    [Route("errors")]
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
