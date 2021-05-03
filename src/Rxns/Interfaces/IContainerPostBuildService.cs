using System;
using System.Reactive;

namespace Rxns.Interfaces
{
    public interface IContainerPostBuildService
    {
        IObservable<Unit> Run(IReportStatus logger, IResolveTypes container);
    }
}
