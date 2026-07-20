using Rxns.DDD.Commanding;
using Rxns.Interfaces;

namespace Rxns.DDD.CQRS
{
    public interface IDomainCommandResult<T> : ICommandResult<T>
    {
        IRxn[] SideEffects { get; }
    }
}
