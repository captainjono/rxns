using System;
using MsLogLevel = Microsoft.Extensions.Logging.LogLevel;
using MsILogger = Microsoft.Extensions.Logging.ILogger;
using Microsoft.Extensions.Logging;
using Rxns.Logging;

namespace Rxns.WebApiNET5.NET5WebApiAdapters
{
    public class RxnsLogDebugProvider : ILoggerProvider
    {
        public MsILogger CreateLogger(string categoryName) => new RxnsLogger(categoryName);

        public void Dispose() { }

        private class RxnsLogger : MsILogger
        {
            private readonly string _category;

            public RxnsLogger(string category) => _category = category;

            public IDisposable BeginScope<TState>(TState state) => NullScope.Instance;

            public bool IsEnabled(MsLogLevel logLevel) => logLevel >= MsLogLevel.Information;

            public void Log<TState>(MsLogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel)) return;
                var msg = formatter(state, exception);
                if (string.IsNullOrEmpty(msg)) return;

                var line = $"[aspnet:{_category}] {msg}";
                if (exception != null)
                    ReportStatus.Log.OnError(exception, line);
                else if (logLevel >= MsLogLevel.Warning)
                    ReportStatus.Log.OnWarning(line);
                else
                    line.LogDebug();
            }

            private class NullScope : IDisposable
            {
                public static readonly NullScope Instance = new NullScope();
                public void Dispose() { }
            }
        }
    }
}
