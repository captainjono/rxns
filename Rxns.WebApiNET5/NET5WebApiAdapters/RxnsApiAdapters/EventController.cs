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
            string payload = null;
            try
            {
                var eventCount = 0;

                using (StreamReader reader = new StreamReader(Request.Body, Encoding.UTF8))
                    payload = await reader.ReadToEndAsync();

                var receivedEvents = ParseAllAsEvents(payload);

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
            catch (UnauthorizedAccessException)
            {
                return Unauthorized();
            }
            catch (Exception e)
            {
                // This catch is the *one* place cross-process Publish failures are
                // observable. Adapters and remote callers see only the HTTP status —
                // they need the actual exception to debug. Surface three ways:
                //   1. ReportStatus.Log via OnError (regardless of DEBUG/RELEASE) so the
                //      portal's local log feed shows it.
                //   2. PublishFailed event on the bus so /api/appstatus/errors picks it up
                //      via LocalAppErrorManager → InMemoryAppStatusStore, making it remotely
                //      visible without server stdout access.
                //   3. Response body via InternalServerError(e) so the client's exception
                //      Message carries the cause when HttpAppStatusServiceClient reads it.
                OnError("While executing publish: {0}", e);
                TryEmitPublishFailed(e, payload);
                return InternalServerError(e);
            }
        }

        private void TryEmitPublishFailed(Exception e, string payload)
        {
            try
            {
                var head = payload == null
                    ? null
                    : payload.Substring(0, Math.Min(payload.Length, 200));
                var inner = e.InnerException == null
                    ? null
                    : e.InnerException.GetType().Name + ": " + e.InnerException.Message;

                var failed = new PublishFailed
                {
                    ExceptionType = e.GetType().Name,
                    Message = e.Message,
                    Inner = inner,
                    PayloadLength = payload?.Length ?? 0,
                    PayloadHead = head,
                    ClientIp = SafeClientIp(),
                    Timestamp = DateTime.UtcNow
                };
                _eventManager.Publish(failed).Until(_ => { });
            }
            catch
            {
                // Last-ditch — never let diagnostics emission shadow the original 500.
            }
        }

        private string SafeClientIp()
        {
            try { return ClientIpAddress(); } catch { return null; }
        }

        private IEnumerable<IRxn> ParseAllAsEvents(string eventsAsJson)
        {
            if (eventsAsJson.IsNullOrWhitespace()) yield break;

            var events = eventsAsJson.Split(new string[] {"\r\n\r"}, StringSplitOptions.RemoveEmptyEntries);

            foreach (var e in events)
            {
                // FromString returns dynamic — when the host is missing a serialiser module
                // (RxnExtensions.DeserialiseImpl never reassigned), this is the JSON string
                // itself, and the implicit conversion to IRxn fails at the yield site with
                // a RuntimeBinderException. The catch in Publish surfaces the cause.
                var evt = (IRxn)_rxnFactory.FromString(e);
                yield return evt;
            }
        }
    }
}


