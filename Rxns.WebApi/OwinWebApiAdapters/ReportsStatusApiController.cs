using System;
using System.Web.Http;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns.WebApi
{
    public class ReportsStatusApiController : ApiController, IReportStatus
    {
        private readonly ReportsStatus _rsImpl;

        public ReportsStatusApiController()
        {
            _rsImpl = new ReportsStatus(GetType().Name);
        }
        
        protected string ClientIpAddress()
        {
            return ((dynamic)Request.Properties["MS_OwinContext"]).Request.RemoteIpAddress;
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

        public void OnError(string exceptionMessage, params object[] args)
        {
            _rsImpl.OnError(exceptionMessage, args);
        }

        public void OnError(Exception innerException, string exceptionMessage, params object[] args)
        {
            _rsImpl.OnError(innerException, exceptionMessage, args);
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

        private bool _isDisposed = false;

        public void Dispose()
        {
            if (!_isDisposed)
            {
                _isDisposed = true;

                _rsImpl.Dispose();
                base.Dispose();
            }
        }
    }
}
