using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;

namespace Rxns.Scheduling
{
    /// <summary>
    /// This task is used for executing another task group
    /// 
    /// A task will execute when all the groups to wait on have finished
    /// </summary>
    public partial class ExecuteGroupTask : SchedulableTask
    {
        public override string ReporterName { get { return String.Format("Execute<{0}>", Group); } }

        public ExecuteGroupTask()
        {
            GroupsToWaitOn = new List<string>();
            Options = WaitOptions.Forever;
        }

        public override async Task<ExecutionState> ExecuteTaskAsync(ExecutionState state)
        {
            IObservable<Unit> task = null;
            //lookup groups using the task scheduler
            var taskScheduler = state.Group.TaskScheduler;
            var allGroups = taskScheduler.ScheduledGroups();

            var groupsToWaitOn = allGroups.Where(g => GroupsToWaitOn.Contains(g.Name, StringComparer.CurrentCultureIgnoreCase)).ToArray();
            var executionGroup = allGroups.FirstOrDefault(g => g.Name.Equals(Group, StringComparison.CurrentCultureIgnoreCase));

            //fail if the group to run isnt found
            if (executionGroup == null)
            {
                OnError("Cannot find group '{0}' to execute. Ensure its name is correct and ENABLED the system", Group);
                return state;
            }

            //fail if any of the groups to watch arnt found
            if (groupsToWaitOn.Length != GroupsToWaitOn.Count)
            {
                var requiredGroups = groupsToWaitOn.Select(g => g.Name);

                OnError("Cannot find the following groups to wait on: {0}",
                    String.Join(",", GroupsToWaitOn.Select(groupName => requiredGroups.Contains(groupName, StringComparer.CurrentCultureIgnoreCase))));

                return state;
            }

            //now we have all the information required
            //wait if we need to
            if (GroupsToWaitOn.Count > 0)
            {
                //this is a dirty hack, but my only choice. for some reason i cant get await on .net4 working here
                //ive followed all the guides but i cant the error "cannot await" and all the MS help fails
                //http://blogs.msdn.com/b/bclteam/p/asynctargetingpackkb.aspx
                //Im just lucky that i can put this is run on another thread by default
                WaitOnOtherGroups(state.Group, groupsToWaitOn).Wait();

                //execute the group
                task = taskScheduler.Run(executionGroup, state);
            }
            else
            {
                //execute the group
                task = taskScheduler.Run(executionGroup, state);
            }

            if (IsSynchronous)
            {
                task.Wait();
                if (!executionGroup.RanToCompletion())
                    throw new Exception(String.Format("'{0}' encourntered an error", executionGroup.Name));
            }

            return state;
        }

        protected override void BindToState(ExecutionState state)
        {
            base.BindToState(state);

            foreach (var input in InputParameters)
                if (input.Value != null)
                    state.Variables.AddOrReplace(input.Parameter, input.Value);
        }

        private IObservable<bool> WaitOnOtherGroups(ISchedulableTaskGroup parent, IEnumerable<ISchedulableTaskGroup> groups)
        {
            var matchingGroups = groups.Where(g => GroupsToWaitOn.Contains(g.Name)).ToList();
            var allRunning = matchingGroups.WhenAllRunning(false);

            Debug.WriteLine("Fix waiting for parent group");
            //if (Options == WaitOptions.ParentGroup)
              //  allRunning.CancelsWith(parent.IsWaiting, true);

            return allRunning;
        }
    }
}
