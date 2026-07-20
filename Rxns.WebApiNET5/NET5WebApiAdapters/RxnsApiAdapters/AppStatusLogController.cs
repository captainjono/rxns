using System;
using System.Reactive.Linq;
using Microsoft.AspNetCore.Mvc;
using Rxns.Health.AppStatus;

namespace Rxns.WebApiNET5.NET5WebApiAdapters.RxnsApiAdapters
{
    /// <summary>
    /// Generic REST surface over IAppStatusLogReader — used by the rxns-support portal
    /// (and any other client) to read recent logs/errors/stats for a registered system.
    /// Filter by ?systemName= to scope to one app; omit to see all.
    /// </summary>
    [Route("api/appstatus")]
    public class AppStatusLogController : ReportsStatusApiController
    {
        private readonly IAppStatusLogReader _reader;

        public AppStatusLogController(IAppStatusLogReader reader)
        {
            _reader = reader;
        }

        [HttpGet("systems")]
        public IActionResult GetSystems()
        {
            return this.ReportExceptions(() => (IActionResult)Ok(_reader.GetRegisteredSystems()))
                ?? StatusCode(500, "log reader unavailable");
        }

        [HttpGet("log")]
        public IActionResult GetLog(string systemName = null, string level = null, DateTime? since = null, int skip = 0, int take = 200)
        {
            return this.ReportExceptions(() => (IActionResult)Ok(_reader.GetLog(systemName, level, since, skip, take)))
                ?? StatusCode(500, "log reader unavailable");
        }

        [HttpGet("errors")]
        public IActionResult GetErrors(string systemName = null, DateTime? since = null, int skip = 0, int take = 200)
        {
            return this.ReportExceptions(() => (IActionResult)Ok(_reader.GetErrors(systemName, since, skip, take)))
                ?? StatusCode(500, "log reader unavailable");
        }

        [HttpGet("stats")]
        public IActionResult GetStats(string systemName = null)
        {
            return this.ReportExceptions(() => (IActionResult)Ok(_reader.GetStats(systemName)))
                ?? StatusCode(500, "log reader unavailable");
        }
    }
}
