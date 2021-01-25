using System;
using System.Collections.Generic;
using System.Reactive;
using Rxns.Interfaces;

namespace Rxns.Scheduling
{
    /// <summary>
    /// Represents a group of tasks that can be scheduled to execute
    /// </summary>
    public interface ISchedulableTaskGroup : IReportStatus, IAmStateful, IDisposeResources<ISchedulableTaskGroup>
    {
        /// <summary>
        /// The name used to identify the group
        /// </summary>
        string Name { get; set; }

        /// <summary>
        /// The C# TimeSpan() based schedule that is used to execute the task
        /// </summary>
        TimeSpan? TimeSpanSchedule { get; set; }

        /// <summary>
        /// Only groups which are enabled should be executed
        /// </summary>
        bool IsEnabled { get; set; }

        /// <summary>
        /// The tasks, in execution order, which are executed by this group
        /// </summary>
        List<ISchedulableTask> Tasks { get; set; }

        /// <summary>
        /// Any parameters that can be used by tasks for binding to input parameters
        /// </summary>
        List<OutputParameter> Parameters { get; set; }

        /// <summary>
        /// The scheduler used by this group to schedule its running
        /// </summary>
        ITaskScheduler TaskScheduler { get; set; }

        void Execute(ExecutionState initialState = null);

        bool RanToCompletion();

        bool IsReporting { get; set; }
    }
}
