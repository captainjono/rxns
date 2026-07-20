using System;
using System.Reactive.Concurrency;
using Rxns.DDD.Tenant;
using Rxns.Interfaces;
using Rxns.Interfaces.Reliability;
using Rxns.Logging;

namespace Rxns.DDD
{
    public class ReportsStatusBatchingViewProcessor : BatchingViewProcessor, IReportStatus
    {
        private bool _isDisposed = false;
        private readonly IReportStatus _rsImpl;

        public ReportsStatusBatchingViewProcessor(ITenantDatabaseFactory contextFactory, IReliabilityManager reliably, IScheduler dbScheduler = null)
            : base(contextFactory, reliably, dbScheduler)
        {
            _rsImpl = new ReportsStatus(GetType().Name);
        }

        public IObservable<LogMessage<Exception>> Errors
        {
            get { return _rsImpl.Errors; }
        }

        public IObservable<LogMessage<string>> Information
        {
            get { return _rsImpl.Information; }
        }

        public string ReporterName
        {
            get { return GetType().Name; }
        }

        public void OnError(Exception exception)
        {
            _rsImpl.OnError(exception);
        }
        
        public void OnInformation(string info, params object[] args)
        {
            _rsImpl.OnInformation(info, args);
        }

        public void OnWarning(string info, params object[] args)
        {
            _rsImpl.OnWarning(info, args);
        }

        public void OnVerbose(string info, params object[] args)
        {
            _rsImpl.OnVerbose(info, args);
        }

        public void OnDispose(IDisposable me)
        {
            _rsImpl.OnDispose(me);
        }


        public void Dispose()
        {
            if (!_isDisposed)
            {
                _isDisposed = true;

                _rsImpl.Dispose();
            }
        }
    }
}
