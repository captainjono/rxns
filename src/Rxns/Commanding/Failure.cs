using System;

namespace Rxns.DDD.Commanding
{
    /// <summary>
    /// A command which has failed
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class Failure<T> : ICommandResult<T> where T : IUniqueRxn
    {
        public Failure(T request, Exception cause)
        {
            Error = cause;
            Result = request;
            InResponseTo = request.Id;
        }

        public bool WasSuccessful
        {
            get { return false; }
        }

        public Exception Error { get; private set; }

        public T Result { get; private set; }
        public string InResponseTo { get; private set; }
    }
}
