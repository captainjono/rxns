using System;
using Rxns.Hosting.Cluster;
using Rxns.Microservices;

namespace Rxns.Hosting
{
    public interface IRxnAppProcessContext
    {
        string[] args { get; } //todo: convert to IRxnCfg class ? how to abstract platform args away but still pass through?
        IObservable<IRxnAppContext> Start(bool shouldStartRxns = true, IAppContainer container = null);
        void Terminate();

        IObservable<ProcessStatus> Status { get; }
    }


    public interface IRxnAppProcessFactory
    {
        IObservable<IRxnAppContext> Create(IRxnHostableApp app, IRxnHostManager hostManager, string reactorName, RxnMode mode = RxnMode.InProcess);
        IObservable<IRxnAppContext> Create(IRxnHostableApp app, IRxnHostManager hostManager, string[] args = null, RxnMode mode = RxnMode.InProcess);
        RxnMode DetectMode(IRxnAppCfg original);
    }
}
