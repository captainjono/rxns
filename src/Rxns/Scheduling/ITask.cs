using System;
using System.Reactive;

namespace Rxns.Scheduling
{
    public interface ITask
    {
        IObservable<Unit> Execute();
    }

    public interface ITask<T> : ITask<T, T>
    {
    }

    public interface ITask<T, Tr>
    {
        /// <summary>
        /// Executes a task based on the parameters configured
        /// </summary>
        /// <param name="state">The state which can be referenced, or updated by the task, and is shared amongst other tasks</param>
        /// <returns>The state as modified by the task.</returns>
        T Execute(Tr state);
    }
}
