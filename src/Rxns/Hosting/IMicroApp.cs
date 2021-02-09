using System;
using Rxns.Interfaces;

namespace Rxns.Hosting
{
    public interface IMicroApp : IReportStatus
    {
        string[] Args { get; }
        IObservable<IRxnAppContext> Start();
    }
}
