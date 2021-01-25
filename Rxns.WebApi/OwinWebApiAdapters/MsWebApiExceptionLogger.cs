using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http.ExceptionHandling;
using Rxns.Logging;

namespace Rxns.WebApi.MsWebApiFeatures
{
    public class MsWebApiRxnExceptionLogger : ReportsStatus, IExceptionLogger, IExceptionHandler
    {
        public Task LogAsync(ExceptionLoggerContext context, CancellationToken cancellationToken)
        {
            OnError(context.Exception);

            return new Task(() => { });
        }

        public Task HandleAsync(ExceptionHandlerContext context, CancellationToken cancellationToken)
        {
            OnError(context.Exception);

            return new Task(() => { });
        }
    }
}
