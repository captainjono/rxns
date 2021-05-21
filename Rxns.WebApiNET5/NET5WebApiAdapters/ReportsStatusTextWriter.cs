using System;
using System.IO;
using System.Text;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns.WebApiNET5.NET5WebApiAdapters
{
    public class ReportsStatusTextWriter : TextWriter, IReportStatus
    {

        public override void WriteLine(string value)
        {
            value = value.Replace("{", "(").Replace("}", ")");

            if(value.ToLower().Contains("error"))
                OnError(new Exception(value));
            else
                OnInformation(value);

            base.WriteLine(value);
        }

        public override Encoding Encoding
        {
            get { return Encoding.UTF8; }
        }

        private ReportsStatus _rsImpl;

        public ReportsStatusTextWriter()
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

        public new void Dispose()
        {
            if (!_isDisposed)
            {
                _isDisposed = true;

                _rsImpl.Dispose();
                base.Dispose();
            }
        }

        public override void Write(char value)
        {
            
        }
    }
}
