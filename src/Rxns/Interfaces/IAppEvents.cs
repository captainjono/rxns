using System;
using System.Reactive;

namespace Rxns.Interfaces
{
    public interface IAppEvents
    {
        IObservable<Unit> OnStart { get; }
    }
}
