using System;
using System.Reactive;

namespace Rxns.Interfaces
{
    public interface IContainerEvents
    {
        IObservable<Unit> OnStart { get; }
    }
}
