using System;

namespace Rxns.DDD.Commanding
{
    /// <summary>
    /// The result of a command
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public interface ICommandResult<out T> : IRxnResult
    {
        bool WasSuccessful { get; }
        Exception Error { get; }
        T Result { get; }
    }
}
