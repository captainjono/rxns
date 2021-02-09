using System;
using System.Reactive.Linq;
using Rxns.Logging;

namespace Rxns.Hosting
{
    public class RxnMicroApp : ReportsStatus, IMicroApp
    {
        private readonly IObservable<IRxnAppContext> _app;

        public RxnMicroApp(IObservable<IRxnAppContext> app, string[] args = null)
        {
            Args = args ?? new string[0];
            _app = app;
        }

        public string[] Args { get; }

        public IObservable<IRxnAppContext> Start()
        {
            return _app;
        }
    }
}
