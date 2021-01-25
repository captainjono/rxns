using System;
using System.Reactive;
using Rxns.Interfaces;

namespace Rxns.Hosting
{
    public interface IRxnHost : IReportStatus
    {
        IDisposable Start();

        void Restart(string version = null);

        IObservable<Unit> Install(string installer, string version);

        IObservable<IRxnAppContext> Run(IRxnHostableApp app, IRxnAppCfg cfg);

        string Name { get; set; }
    }
}
