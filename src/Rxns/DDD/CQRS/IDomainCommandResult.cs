using Rxns.DDD.Commanding;
using Rxns.Interfaces;

namespace Rxns.CQRS
{
    public interface IDomainCommandResult<T> : ICommandResult<T>
    {
        IRxn[] SideEffects { get; }
    }
}
