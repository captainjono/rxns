using System;
using System.Reactive;
using Rxns.Interfaces;
using Rxns.Microservices;

namespace Rxns.Hosting
{
    public interface IRxnHostReadyToRun : IRxnHost
    {
        IObservable<IRxnAppContext> Run(IAppContainer container = null);
    }

    public interface IRxnHost : IReportStatus
    {
        IDisposable Start();

        void Restart(string version = null);

        IObservable<Unit> Install(string installer, string version);

        IObservable<IRxnHostReadyToRun> Stage(IRxnHostableApp app, IRxnAppCfg cfg);

        string Name { get; set; }
    }
}
