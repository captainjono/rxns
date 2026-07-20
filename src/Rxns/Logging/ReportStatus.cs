using System;
using System.Reactive.Disposables;
using Rxns.Interfaces;

namespace Rxns.Logging
{
    public class ReportStatus : ReportsStatus
    {
        public static ReportStatus Log = new ReportStatus();
        public static IDisposable StartupLogger = Disposable.Empty;

        public override string ReporterName
        {
            get { return "AppLog"; }
        }

        public void OnError(string reporterName, Exception exception)
        {
            if (!ReportInformation.HasObservers) return;
            if (exception is AggregateException)
            {
                var exceptions = exception as AggregateException;
                foreach (var e in exceptions.InnerExceptions)
                    ReportExceptions.OnNext(new LogMessage<Exception>() { Reporter = reporterName, Level = LogLevel.Error, Message = e });
            }
            else
            {
                ReportExceptions.OnNext(new LogMessage<Exception>() { Reporter = reporterName, Level = LogLevel.Error, Message = exception });
            }
        }

        public void OnError(string reporterName, string exceptionMessage, params object[] args)
        {
            ReportExceptions.OnNext(new LogMessage<Exception>() { Reporter = reporterName, Level = LogLevel.Error, Message = new Exception(String.Format(exceptionMessage, args)) });
        }

        public void OnError(string reporterName, Exception innerException, string exceptionMessage, params object[] args)
        {
            ReportExceptions.OnNext(new LogMessage<Exception>() { Reporter = reporterName, Level = LogLevel.Error, Message = new Exception(String.Format(exceptionMessage, args), innerException) });
        }

        public void OnInformation(string reporterName, string info, params object[] args)
        {
            OnMessage(reporterName, LogLevel.Info, info, args);
        }

        public void OnWarning(string reporterName, string info, params object[] args)
        {
            OnMessage(reporterName, LogLevel.Warning, info, args);
        }

        public void OnVerbose(string reporterName, string info, params object[] args)
        {
            OnMessage(reporterName, LogLevel.Verbose, info, args);
        }

        private void OnMessage(string reporterName, LogLevel level, string message, params object[] args)
        {
            if (!ReportInformation.HasObservers) return;
            ReportInformation.OnNext(new LogMessage<string>() { Reporter = reporterName, Level = level, Message = String.Format(message, args) });
        }

        public override void Dispose()
        {
        }
    }
}
