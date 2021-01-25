using System;
using Rxns.Logging;

namespace Rxns.Hosting
{
    public class RxnMicroApp : ReportsStatus, IMicroApp
    {
        private readonly IObservable<IDisposable> _app;

        public RxnMicroApp(IObservable<IDisposable> app, string[] args = null)
        {
            Args = args ?? new string[0];
            _app = app;
        }

        public string[] Args { get; }

        public IObservable<IDisposable> Start()
        {
            return _app;
        }
    }
}
