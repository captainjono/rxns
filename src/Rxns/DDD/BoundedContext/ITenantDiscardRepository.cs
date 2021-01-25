using System;
using System.Reactive;
using Rxns.Playback;

namespace Rxns.DDD.BoundedContext
{
    public interface ITenantDiscardRepository
    {
        IObservable<Unit> DiscardPoisonEvent(IDomainEvent @event, Exception with);
        IObservable<Unit> DiscardPoisonTape(string tenant, IFileTapeSource tape, Exception with);
    }
}
