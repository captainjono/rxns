using System;
using System.Collections.Generic;
using System.Linq;
using Rxns.CQRS;
using Rxns.DDD.BoundedContext;
using Rxns.DDD.CQRS;
using Rxns.Interfaces;

namespace Rxns.DDD.Commanding
{
    public class DomainCommandResult<T> : IDomainCommandResult<T>
    {
        public bool WasSuccessful { get { return Error == null; } }
        public Exception Error { get; private set; }
        public string ErrorMessage { get; private set; }
        public T Result { get; private set; }
        public IRxn[] SideEffects { get; private set; }

        public DomainCommandResult() { } 

        public DomainCommandResult(string cmdId, T result, IRxn[] sideEffects)
        {
            InResponseTo = cmdId;
            Result = result;
            SideEffects = sideEffects;
        }

        public DomainCommandResult(string cmdId, Exception exception, IRxn[] sideEffects, T result = default(T))
        {
            InResponseTo = cmdId;
            Result = result;   
            Error = exception;
            SideEffects = sideEffects;
        }

        public static DomainCommandResult<T> FromSuccessfulResult(string cmdId, T result, params IRxn[] sideEffects)
        {
            return new DomainCommandResult<T>(cmdId, result, sideEffects);
        }

        public static DomainCommandResult<T> FromFailureResult(string cmdId, Exception result, params IRxn[] sideEffects)
        {
            return new DomainCommandResult<T>(cmdId, result, sideEffects);
        }

        public string InResponseTo { get; }

        public override string ToString()
        {
            return WasSuccessful ? Result.ToString() : Error.ToString();
        }
    }

    public static class DomainCommandExtensions
    {
        public static void ThrowExceptions<T>(this IDomainCommandResult<T> result)
        {
            if (!result.WasSuccessful) throw new DomainCommandException(result.Error.Message);
        }

        public static DomainCommandResult<T> AsSideEffectsOfResult<T>(this IEnumerable<IDomainEvent> sideEffects, string cmdId, T result)
        {
            return DomainCommandResult<T>.FromSuccessfulResult(cmdId, result,  sideEffects.ToArray());
        }
    }
}
