using System.Collections.Generic;
using System.Reactive;
using Rxns.Interfaces;

namespace Rxns.Scheduling
{
    /// <summary>
    /// Represents a unit of work that can be configured through properties
    /// then run in groups with other tasks to produce a workflow
    /// </summary>
    public interface ISchedulableTask : IReportStatus, ITask<ExecutionState>, IAmStateful
    {
        /// <summary>
        /// if true, the task will finish executing before the next task is started,
        /// otherwise group execution will continue straight away. If this is the last
        /// task in the group, the group will wait for it to finish before finishing.
        /// </summary>
        bool IsSynchronous { get; set; }

        /// <summary>
        /// If all other conditions give the green light, this property can be used to 
        /// override other functionality and stop other tasks from executing.
        ///  </summary>
        bool IsBlocking { get; set; }
        
        /// <summary>
        /// The conditions that will govern the execution of this task
        /// </summary>
        List<ExecutionCondition> Conditions { get; set; }

        /// <summary>
        /// The parameters used as inputs to the tasks execution. for stored procs,
        /// these bind to variables used to call the proc. They can bind to other
        /// other parameters for their values
        /// </summary>
        List<InputParameter> InputParameters { get; set; }

        /// <summary>
        /// These parameters are updated as a result of the execution of the task
        /// </summary>
        List<OutputParameter> OutputParameters { get; set; }
    }
}
