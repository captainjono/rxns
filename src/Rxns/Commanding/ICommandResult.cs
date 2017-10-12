using System;

namespace Rxns.Commanding
{
    /// <summary>
    /// The result of a command
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public interface ICommandResult<out T>
    {
        bool WasSuccessful { get; }
        Exception Error { get; }
        T Result { get; }
    }
}
