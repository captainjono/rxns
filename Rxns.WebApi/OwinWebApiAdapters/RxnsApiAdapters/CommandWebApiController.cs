using System;
using System.Net.Http;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Web.Http;
using Rxns.DDD;
using Rxns.DDD.Commanding;
using Rxns.DDD.CQRS;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns.WebApi
{
    [RoutePrefix("cmd")]
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
        public async Task<IHttpActionResult> Cmd(string tenant, HttpRequestMessage cmd)
        {
            try
            {
                var jsonCmd = await cmd.Content.ReadAsStringAsync();
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
                return Ok(DomainCommandResult<object>.FromFailureResult("unknown", e));
            }
            catch (DomainCommandException e)
            {
                return Ok(DomainCommandResult<object>.FromFailureResult("unknown", e));
            }
            catch (Exception e)
            {

#if !DEBUG
                return InternalServerError(e);
#else
                OnError("While executing: {0}\\cmd {1}\r\n{2}", tenant, cmd.Content.ReadAsStringAsync().WaitR(), e);
                return InternalServerError();
#endif
            }
        }
    }

    [AllowAnonymous]
    [RoutePrefix("anonCmd")]
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
        public async Task<IHttpActionResult> Cmd(string tenant, HttpRequestMessage cmd)
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
                return Ok(DomainCommandResult<object>.FromFailureResult("unknown", e));
            }
            catch (DomainCommandException e)
            {
                return Ok(DomainCommandResult<object>.FromFailureResult("unknown", e));
            }
            catch (Exception e)
            {
#if DEBUG
                return InternalServerError(e);
#else
                OnError("While executing: {0}\\cmd {1}\r\n{2}", tenant, toRun, e);
                return InternalServerError();
#endif
            }
        }
    }
}
