//using System.Threading;
//using System.Threading.Tasks;
//using Rxns.Logging;

//namespace Rxns.WebApiNET5.NET5WebApiAdapters
//{
//    public class MsWebApiRxnExceptionLogger : ReportsStatus, IExceptionLogger, IExceptionHandler
//    {
//        public Task LogAsync(ExceptionLoggerContext context, CancellationToken cancellationToken)
//        {
//            OnError(context.Exception);

//            return new Task(() => { });
//        }

//        public Task HandleAsync(ExceptionHandlerContext context, CancellationToken cancellationToken)
//        {
//            OnError(context.Exception);

//            return new Task(() => { });
//        }
//    }
//}
