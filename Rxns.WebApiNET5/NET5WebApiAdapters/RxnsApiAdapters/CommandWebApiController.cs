using System;
using System.Net.Http;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rxns.DDD;
using Rxns.DDD.Commanding;
using Rxns.DDD.CQRS;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns.WebApiNET5.NET5WebApiAdapters.RxnsApiAdapters
{
    [Route("cmd")]
    //[Authorize]
    public class CommandWebApiController : DomainCommandApiController
    {
        private readonly ICommandFactory _cmdFactory;
        private readonly ICommandService _cmdService;
        
        public CommandWebApiController(ICommandFactory cmdFactory, ICommandService cmdService)
        {
            _cmdFactory = cmdFactory;
            _cmdService = cmdService;
        }


        [Route("{tenant}")]
        [HttpPost]
        public async Task<IActionResult> Cmd(string tenant)
        {
            // Note: this endpoint used to take `HttpRequestMessage cmd` as a parameter,
            // which ASP.NET Core does NOT auto-bind from the request body — it was always
            // null and `cmd.Content.ReadAsStringAsync()` threw NRE before any command
            // could run. Read Request.Body directly instead.
            string jsonCmd = null;
            try
            {
                using (var sr = new global::System.IO.StreamReader(Request.Body))
                    jsonCmd = await sr.ReadToEndAsync();

                var actualCmd = _cmdFactory.FromString(jsonCmd);

                $"Recieved command for {actualCmd.GetType().Name} for {tenant}".LogDebug();

                var result = await ((IObservable<dynamic>)_cmdService.Run(actualCmd));

                return Ok(result);
            }
            catch (UnauthorizedAccessException e)
            {
                return Unauthorized();
            }
            catch (DomainQueryException e)
            {
                return BadRequest(e);
            }
            catch (DomainValidationException e)
            {
                return Ok(new { WasSuccessful = false, Message = e.Message, ErrorType = e.GetType().FullName });
            }
            catch (DomainCommandException e)
            {
                return Ok(new { WasSuccessful = false, Message = e.DomainMessage ?? e.Message, ErrorType = e.GetType().FullName });
            }
            catch (Exception e)
            {

#if !DEBUG
                return InternalServerError(e);
#else
                OnError("While executing: {0}\\cmd {1}\r\n{2}", tenant, jsonCmd ?? "<unread>", e);
                return InternalServerError(e);
#endif
            }
        }
    }

    [AllowAnonymous]
    [Route("anonCmd")]
    public class AnonymousCommandController : DomainCommandApiController
    {
        private readonly ICommandFactory _cmdFactory;
        private readonly ICommandService _cmdService;

        public AnonymousCommandController(ICommandFactory cmdFactory, ICommandService cmdService)
        {
            _cmdFactory = cmdFactory;
            _cmdService = cmdService;
        }

        [Route("{tenant}")]
        [HttpPost]
        public async Task<IActionResult> Cmd(string tenant, HttpRequestMessage cmd)
        {
            object toRun = null;

            try
            {
                var jsonCmd = cmd.Content.ReadAsStringAsync().WaitR();
                var result = await (_cmdService.Run(jsonCmd) as IObservable<dynamic>);

                return Ok(result);
            }
            catch (UnauthorizedAccessException e)
            {
                return Unauthorized();
            }
            catch (DomainValidationException e)
            {
                return Ok(new { WasSuccessful = false, Message = e.Message, ErrorType = e.GetType().FullName });
            }
            catch (DomainCommandException e)
            {
                return Ok(new { WasSuccessful = false, Message = e.DomainMessage ?? e.Message, ErrorType = e.GetType().FullName });
            }
            catch (Exception e)
            {
#if DEBUG
                return InternalServerError(e);
#else
                OnError("While executing: {0}\\cmd {1}\r\n{2}", tenant, toRun, e);
                return InternalServerError(e);
#endif
            }
        }
    }
}
