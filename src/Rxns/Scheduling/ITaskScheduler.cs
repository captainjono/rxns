using System;
using System.Collections.Generic;
using System.Reactive;

namespace Rxns.Scheduling
{
    public interface ITaskScheduler
    {
        IObservable<bool> IsStarted { get; }
        void Clear();
        void Start();
        void Stop();
        void Pause();
        void Resume();
        IObservable<Unit> Run(ISchedulableTaskGroup group, ExecutionState state = null);
        void Schedule(IEnumerable<ISchedulableTaskGroup> groups);
        void Schedule(ISchedulableTaskGroup taskGroup);
        void UnSchedule(ISchedulableTaskGroup[] groups);
        /// <summary>
        /// Returns a list of all the groups that have been scheduled
        /// </summary>
        /// <returns>A list of groups, deffered or scheudled</returns>
        ISchedulableTaskGroup[] ScheduledGroups();
    }
}
