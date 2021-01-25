using System;
using Rxns.DDD.Commanding;
using Rxns.CQRS;

namespace Rxns.DDD.CQRS
{
    public interface ICommandService
    {
        IObservable<DomainQueryResult<T>> Run<T>(IDomainQuery<T> query);
        IObservable<DomainCommandResult<T>> Run<T>(IDomainCommand<T> cmd);
        IObservable<object> Run(IServiceCommand cmd);
        IObservable<ICommandResult> Run(string cmd);
    }

}
