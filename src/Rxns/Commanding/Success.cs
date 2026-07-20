using System;

namespace Rxns.DDD.Commanding
{
    /// <summary>
    /// The command which was successful
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class Success<T> : ICommandResult<T>
    {
        public Success(string cmdId, T result)
        {
            InResponseTo = cmdId;
            Result = result;
        }

        public bool WasSuccessful
        {
            get { return true; }
        }

        public Exception Error
        {
            get { return null; }
        }

        public T Result { get; private set; }
        public string InResponseTo { get; }
    }
}
