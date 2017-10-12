using System;

namespace Rxns.Commanding
{
    /// <summary>
    /// A command which has failed
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class Failure<T> : ICommandResult<T>
    {
        public Failure(T request, Exception cause)
        {
            Error = cause;
            Result = request;
        }

        public bool WasSuccessful
        {
            get { return false; }
        }

        public Exception Error { get; private set; }

        public T Result { get; private set; }
    }
}
