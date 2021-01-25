using System;

namespace Rxns.Scheduling
{
    public interface ITaskProvider
    {
        IObservable<ISchedulableTaskGroup[]> GetTasks();
    }
}
