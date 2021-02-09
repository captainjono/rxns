using System;
using System.Collections.Generic;
using System.Net.Http;
using Microsoft.AspNetCore.Mvc;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns.WebApiNET5.NET5WebApiAdapters.RxnsApiAdapters
{
    [Route("events")]
    //[Authorize]
    public class EventController : DomainCommandApiController
    {
        private readonly IRxnManager<IRxn> _eventManager;

        public EventController(IRxnManager<IRxn> eventManager)
        {
            _eventManager = eventManager;
        }

        [Route("publish")]
        [HttpPost]
        public IActionResult Publish(HttpRequestMessage cmd)
        {
            try
            {
                var eventCount = 0;

                var receivedEvents = ParseAllAsEvents(cmd.Content.ReadAsStringAsync().WaitR());
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
            var events = eventsAsJson.Split(new string[] { "\r\n\r" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var e in events)
            {
                var evt = e.Deserialise<RLM>();
                yield return evt;
            }
        }
    }
}


