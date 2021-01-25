using Rxns.DDD.Commanding;
using Rxns.Interfaces;

namespace Rxns.CQRS
{
    public class CommandSuccess<T> : Success<T>, IDomainCommandResult<T>
    {
        public IRxn[] SideEffects { get; private set; }

        public CommandSuccess(string cmdId, T result, IRxn[] sideEffects = null)
            : base(cmdId, result)
        {
            SideEffects = sideEffects ?? new IRxn[] {};
        }
    }
}
