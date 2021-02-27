using System;
using System.Reactive;
using Rxns.DDD.BoundedContext;
using Rxns.Playback;

namespace RxnsDemo.AzureB2C.Rxns
{
    public interface ITenantDiscardRepository
    {
        IObservable<Unit> DiscardPoisonEvent(IDomainEvent @event, Exception with);
        IObservable<Unit> DiscardPoisonTape(string tenant, IFileTapeSource tape, Exception with);
    }
}
