using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http.ExceptionHandling;

namespace Rxns.Microservices
{
    public class ExceptionLogger : IExceptionLogger
    {
        public virtual Task LogAsync(ExceptionLoggerContext context,
                                     CancellationToken cancellationToken)
        {
            if (!ShouldLog(context))
            {
                return Task.FromResult(0);
            }

            return LogAsyncCore(context, cancellationToken);
        }

        public virtual Task LogAsyncCore(ExceptionLoggerContext context,
                                         CancellationToken cancellationToken)
        {
            LogCore(context);
            return Task.FromResult(0);
        }

        public virtual void LogCore(ExceptionLoggerContext context)
        {
        }

        public virtual bool ShouldLog(ExceptionLoggerContext context)
        {
            var exceptionData = context.ExceptionContext.Exception.Data;

            if (!exceptionData.Contains("MS_LoggedBy"))
            {
                if (!exceptionData.Contains("MS_LoggedBy"))
                    exceptionData.Add("MS_LoggedBy", new List<object>());
            }

            var loggedBy = ((ICollection<object>)exceptionData["MS_LoggedBy"]);

            if (!loggedBy.Contains(this))
            {
                loggedBy.Add(this);
                return true;
            }

            return false;
        }
    }
}
