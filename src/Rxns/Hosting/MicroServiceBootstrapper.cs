//using System;
//using Rxns.AutoUpdate.Interface;
//using Rxns.Interfaces;
//using Rxns.Logging;

//namespace Rxns.Hosting
//{
//    public class MicrosServiceBootstrapper : Updatable, IReportStatus
//    {
//        private bool _isDisposed = false;
//        private readonly ReportsStatus _rsImpl;
//        private IMicroApp _app;
//        private IAppSetup _installer;

//        public MicrosServiceBootstrapper(IMicroApp app)
//        {
//            _app = app;
//            _rsImpl = new ReportsStatus(this.GetType().Name);

//            //Information.Do(msg => LogMessage(msg.ToString())).Until().DisposedBy(this);
//            //Errors.Do(msg => LogMessage(msg.ToString())).Until().DisposedBy(this);
//        }

//        public override void Start()
//        {
//            try
//            {
//                OnInformation("Starting");

//                _rsImpl.ReportsOn(_app).DisposedBy(_app);
//                _installer = _app.Start();

//                OnInformation("Started");
//            }
//            catch (Exception ex)
//            {
//                OnError(ex);
//                throw;
//            }
//        }

//        private void DisposeManager()
//        {
//            if (_app != null)
//            {
//                _app.Dispose();
//                _app = null;
//            }
//        }

//        public override void Install()
//        {
//            try
//            {
//                using (var installer = _installer)
//                {
//                    installer.Install();
//                }
//            }
//            finally
//            {
//                DisposeManager();
//            }

//        }

//        public override void Stop()
//        {
//            try
//            {
//                OnInformation("Stopping");

//                DisposeManager();

//                OnInformation("Stopped");
//            }
//            catch (Exception ex)
//            {
//                _app = null;
//                OnError(ex);
//            }
//        }

//        #region Logging
//        #endregion

//        public IObservable<LogMessage<Exception>> Errors
//        {
//            get { return _rsImpl.Errors; }
//        }

//        public IObservable<LogMessage<string>> Information
//        {
//            get { return _rsImpl.Information; }
//        }

//        public string ReporterName
//        {
//            get { return GetType().Name; }
//        }

//        public void OnError(Exception exception)
//        {
//            _rsImpl.OnError(exception);
//        }

//        public void OnError(string exceptionMessage, params object[] args)
//        {
//            _rsImpl.OnError(exceptionMessage, args);
//        }

//        public void OnError(Exception innerException, string exceptionMessage, params object[] args)
//        {
//            _rsImpl.OnError(innerException, exceptionMessage, args);
//        }

//        public void OnInformation(string info, params object[] args)
//        {
//            _rsImpl.OnInformation(info, args);
//        }

//        public void OnWarning(string info, params object[] args)
//        {
//            _rsImpl.OnWarning(info, args);
//        }

//        public void OnVerbose(string info, params object[] args)
//        {
//            _rsImpl.OnVerbose(info, args);
//        }

//        public void OnDispose(IDisposable me)
//        {
//            _rsImpl.OnDispose(me);
//        }


//        public new void Dispose()
//        {
//            if (!_isDisposed)
//            {
//                _isDisposed = true;

//                _rsImpl.Dispose();
//                base.Dispose();
//            }
//        }
//    }

//}
