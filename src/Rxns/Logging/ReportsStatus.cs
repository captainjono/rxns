using System;
using System.Collections.Generic;
using System.Reactive.Subjects;
using System.Reflection;
using System.Threading.Tasks;
using Rxns.Interfaces;

namespace Rxns.Logging
{
    /// <summary>
    /// The base implmentation the status reporter
    /// </summary>
    public class ReportsStatus : IReportStatus
    {
        public readonly ReplaySubject<LogMessage<Exception>> ReportExceptions = new ReplaySubject<LogMessage<Exception>>(0);
        public readonly ReplaySubject<LogMessage<string>> ReportInformation = new ReplaySubject<LogMessage<string>>(0);

        protected string _reporterName;
        public virtual string ReporterName { get { return _reporterName; } }

        protected readonly List<IDisposable> ManagedResources = new List<IDisposable>();
        protected bool IsDisposed;

        /// <summary>
        /// Disposes the object with the rest of the managed resources OnDispose()
        /// </summary>
        /// <param name="me"></param>
        public virtual void OnDispose(IDisposable me)
        {
            ManagedResources.Add(me);
        }

        public ReportsStatus()
        {
            _reporterName = GetType().Name;
        }

        public ReportsStatus(string reporterName)
        {
            _reporterName = reporterName;
        }

        public IObservable<LogMessage<Exception>> Errors
        {
            get { return ReportExceptions; }
        }

        public IObservable<LogMessage<string>> Information
        {
            get { return ReportInformation; }
        }

        public void OnError(Exception exception)
        {
            if (!ReportExceptions.HasObservers) return;

            if (exception is AggregateException)
            {
                var exceptions = (AggregateException)exception;

                foreach (var e in exceptions.InnerExceptions)
                    ReportExceptions.OnNext(new LogMessage<Exception>() { Reporter = ReporterName, Level = LogLevel.Error, Message = e });
            }
            else if (exception is TaskCanceledException)
            {
                var exceptions = (TaskCanceledException)exception;

                if (exceptions.Task != null && exceptions.Task.Exception != null)
                {
                    foreach (var e in exceptions.Task.Exception.InnerExceptions)
                        ReportExceptions.OnNext(new LogMessage<Exception>() { Reporter = ReporterName, Level = LogLevel.Error, Message = e });
                }
                else
                {
                    ReportExceptions.OnNext(new LogMessage<Exception>() { Reporter = ReporterName, Level = LogLevel.Error, Message = exception });
                }
            }
            else if (exception is ReflectionTypeLoadException)
            {
                var exceptions = (ReflectionTypeLoadException)exception;

                if (exceptions.LoaderExceptions.Length == 0)
                    ReportExceptions.OnNext(new LogMessage<Exception>() { Reporter = ReporterName, Level = LogLevel.Error, Message = exception });

                foreach (var e in exceptions.LoaderExceptions)
                    OnError("Cannot load type: {0}", e);
            }
            else if (exception is TargetInvocationException)
            {
                var exceptions = (TargetInvocationException)exception;

                if (exceptions.InnerException != null)
                    ReportExceptions.OnNext(new LogMessage<Exception>() { Reporter = ReporterName, Level = LogLevel.Error, Message = exception.InnerException });
                else
                    ReportExceptions.OnNext(new LogMessage<Exception>() { Reporter = ReporterName, Level = LogLevel.Error, Message = exception });
            }
            else
            {
                ReportExceptions.OnNext(new LogMessage<Exception>() { Reporter = ReporterName, Level = LogLevel.Error, Message = exception });
            }
        }

        public void OnError(string exceptionMessage, params object[] args)
        {
            ReportExceptions.OnNext(new LogMessage<Exception>() { Reporter = ReporterName, Level = LogLevel.Error, Message = new Exception(String.Format(exceptionMessage, args)) });
        }

        public void OnError(Exception innerException, string exceptionMessage, params object[] args)
        {
            ReportExceptions.OnNext(new LogMessage<Exception>() { Reporter = ReporterName, Level = LogLevel.Error, Message = new Exception(String.Format(exceptionMessage, args), innerException) });
        }

        public void OnInformation(string info, params object[] args)
        {
            OnMessage(LogLevel.Info, info, args);
        }

        public void OnWarning(string info, params object[] args)
        {
            OnMessage(LogLevel.Warning, info, args);
        }
        public void OnVerbose(string info, params object[] args)
        {
            OnMessage(LogLevel.Verbose, info, args);
        }

        private void OnMessage(LogLevel level, string message, params object[] args)
        {
            if (!ReportInformation.HasObservers) return;

            ReportInformation.OnNext(new LogMessage<string>() { Reporter = ReporterName, Level = level, Message = String.Format(message, args) });
        }

        /// <summary>
        /// Disposes of the managed resources as well as the reporting channels
        /// </summary>
        public virtual void Dispose()
        {
            if (!IsDisposed)
            {
                IsDisposed = true;
                ManagedResources.DisposeAll();

                ReportInformation.OnCompleted();
                ReportExceptions.OnCompleted();

                ReportInformation.Dispose();
                ReportExceptions.Dispose();
            }
        }
    }
}
