using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns.WebApiNET5.NET5WebApiAdapters.RxnsApiAdapters
{
    //[Authorize]
    public class EventController : DomainCommandApiController
    {
        private readonly IRxnManager<IRxn> _eventManager;
        private readonly ICommandFactory _rxnFactory;

        public EventController(IRxnManager<IRxn> eventManager, ICommandFactory rxnFactory)
        {
            _eventManager = eventManager;
            _rxnFactory = rxnFactory;
        }

        [Route("events/publish")]
        [HttpPost]
        public async Task<IActionResult> Publish()
        {
            try
            {
                var eventCount = 0;

                IEnumerable<IRxn> receivedEvents = null;
                using (StreamReader reader = new StreamReader(Request.Body, Encoding.UTF8))
                    receivedEvents = ParseAllAsEvents(await reader.ReadToEndAsync());

                var ip = ClientIpAddress();

                receivedEvents.ForEach(e =>
                {
                    if (e is RLM l)
                    {

                        l.S = $"{ip}][{l.S}";
                    }

                    _eventManager.Publish(e).Until(OnError);
                    eventCount++;
                });

                OnInformation("Published '{0}' events from tenant '{1}'".FormatWith(eventCount, User?.Identity?.Name ?? "unknown"));

                return Ok("published '{0}' events".FormatWith(eventCount));
            }
            catch (UnauthorizedAccessException e)
            {
                return Unauthorized();
            }
            catch (Exception e)
            {

#if DEBUG
                
                return InternalServerError(e);
#else
                OnError("While executing publish: {0}", e);
                return InternalServerError();
#endif
            }
        }

        private IEnumerable<IRxn> ParseAllAsEvents(string eventsAsJson)
        {
            if (eventsAsJson.IsNullOrWhitespace()) yield break;

            var events = eventsAsJson.Split(new string[] {"\r\n\r"}, StringSplitOptions.RemoveEmptyEntries);

            foreach (var e in events)
            {
                var evt = _rxnFactory.FromString(e);
                yield return evt;
            }
        }
    }
}


