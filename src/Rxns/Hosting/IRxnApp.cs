using System;
using Rxns.Microservices;

namespace Rxns.Hosting
{
    public interface IRxnApp : IDisposable
    {
        IRxnDef Definition { get; }
        IAppSetup Installer { get; }
        IObservable<IRxnAppContext> Start(bool startRxns = true, IAppContainer container = null);
        //need to implement a change version function like Upgrade or Switch(version) ?
        //or is this a concern of the other host?
    }
}
