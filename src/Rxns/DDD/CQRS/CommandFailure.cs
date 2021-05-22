using System;
using Rxns.DDD.Commanding;
using Rxns.Interfaces;

namespace Rxns.DDD.CQRS
{
    public class CommandFailure<T> : Failure<T>, IDomainCommandResult<T> where T : DDD.Commanding.IUniqueRxn
    {
        public IRxn[] SideEffects { get; private set; }

        public CommandFailure(T result, Exception error)
            : base(result, error)
        {
        }
    }
}
