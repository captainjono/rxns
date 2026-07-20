using System.Collections.Generic;

namespace Rxns.Scheduling
{
    public class ExecutionState
    {
        /// <summary>
        /// The scheduler used to execute the task
        /// </summary>
        public ISchedulableTaskGroup Group { get; set; }
        public Dictionary<string, object> Variables = new Dictionary<string, object>();

    }
}
