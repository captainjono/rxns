using System;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading.Tasks;

namespace Rxns.Scheduling
{
    /// <summary>
    /// A task that can be part of a schedulable workflow, whos execute method can be specificed in the constructor
    /// </summary>
    public class DyanmicSchedulableTask : SchedulableTask
    {
        private Func<ExecutionState, ExecutionState> _executeAction;
        public DyanmicSchedulableTask(Func<ExecutionState, ExecutionState> executeAction)
        {
            _executeAction = executeAction;
        }

        public override Task<ExecutionState> ExecuteTaskAsync(ExecutionState state)
        {
            return state.ToObservable().Select(s =>_executeAction(s)).ToTask();
        }
    }
}
