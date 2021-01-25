using System;
using Rxns.DDD;
using Rxns.DDD.CQRS;
using Rxns.Interfaces;

namespace Rxns.Hosting
{
    public interface IRxnAppContext : IManageResources, IRxnAppProcessContext
    {
        IAppSetup Installer { get; }

        ICommandService CmdService { get; }

        IAppCommandService AppCmdService { get; }

        IRxnManager<IRxn> RxnManager { get; }

        IResolveTypes Resolver { get; }

        IObservable<ProcessStatus> Status { get; }

        IRxnHostableApp App { get; }

        //IScheduler

        //Metrics client ?
    }
}
