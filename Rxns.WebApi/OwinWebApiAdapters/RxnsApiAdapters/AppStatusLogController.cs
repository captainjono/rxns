using System;
using System.Web.Http;
using Rxns.Health.AppStatus;

namespace Rxns.WebApi.MsWebApiAdapters.RxnsApiAdapters
{
    /// <summary>
    /// OWIN parity for the AspNetCore AppStatusLogController. REST only — the SignalR
    /// hub (AppStatusLogHub) is NET5-only by design. Owin hosts get REST polling.
    /// </summary>
    [RoutePrefix("api/appstatus")]
    public class AppStatusLogController : ReportsStatusApiController
    {
        private readonly IAppStatusLogReader _reader;

        public AppStatusLogController(IAppStatusLogReader reader)
        {
            _reader = reader;
        }

        [Route("systems")]
        [HttpGet]
        public IHttpActionResult GetSystems()
        {
            return this.ReportExceptions(() => (IHttpActionResult)Ok(_reader.GetRegisteredSystems()))
                ?? InternalServerError(new InvalidOperationException("log reader unavailable"));
        }

        [Route("log")]
        [HttpGet]
        public IHttpActionResult GetLog(string systemName = null, string level = null, DateTime? since = null, int skip = 0, int take = 200)
        {
            return this.ReportExceptions(() => (IHttpActionResult)Ok(_reader.GetLog(systemName, level, since, skip, take)))
                ?? InternalServerError(new InvalidOperationException("log reader unavailable"));
        }

        [Route("errors")]
        [HttpGet]
        public IHttpActionResult GetErrors(string systemName = null, DateTime? since = null, int skip = 0, int take = 200)
        {
            return this.ReportExceptions(() => (IHttpActionResult)Ok(_reader.GetErrors(systemName, since, skip, take)))
                ?? InternalServerError(new InvalidOperationException("log reader unavailable"));
        }

        [Route("stats")]
        [HttpGet]
        public IHttpActionResult GetStats(string systemName = null)
        {
            return this.ReportExceptions(() => (IHttpActionResult)Ok(_reader.GetStats(systemName)))
                ?? InternalServerError(new InvalidOperationException("log reader unavailable"));
        }
    }
}
