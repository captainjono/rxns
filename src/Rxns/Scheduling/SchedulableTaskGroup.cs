using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Rxns.Cloud;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns.Scheduling
{
    /// <summary>
    /// This class encapsulates a series of tasks into a workflow, provides a unified state which the 
    /// tasks can leverage off to provide inputs and outputs.
    /// 
    /// Groups must be enabled to be considered "active" by the scheduler or other components. then can 
    /// either be scheduled with a Time  (Cron or Timespan) or they can be referenced by other groups 
    /// and executed ad-hoc.
    /// 
    /// The defaultscheduler is a newthreadsheduler to allow tasks to run in an isolated environment
    /// </summary>
    public partial class SchedulableTaskGroup : ReportsStatus, ISchedulableTaskGroup//, IAmReactive
    {
        /// <summary>
        /// The amount of tasks that successfully completed their tasks
        /// </summary>
        public RunningResult Result { get; set; }

        /// <summary>
        /// The name of the group
        /// </summary>
        public override string ReporterName { get { return String.Format("SG<{0}>", Name); } }

        /// <summary>
        /// The default scheduler is a new thread scheduler (for running tasks)
        /// </summary>
        public IScheduler DefaultScheduler { get; set; }
        /// <summary>
        /// The scheduler used in the state to allow tasks to manipulate schedules
        /// This should be the same as scheduler which launched this group
        /// </summary>
        public ITaskScheduler TaskScheduler { get; set; }

        private readonly BehaviorSubject<bool> _isRunning;
        /// <summary>
        /// If the group is currently running tasks or not
        /// </summary>
        public IObservable<bool> IsRunning { get { return _isRunning; } }

        private readonly BehaviorSubject<bool> _isWaiting;
        /// <summary>
        /// If the group is idle but waiting for tasks to complete
        /// </summary>
        public IObservable<bool> IsWaiting { get { return _isWaiting; } }

        /// <summary>
        /// The state provided to tasks
        /// </summary>
        private ExecutionState _state;

        /// <summary>
        /// Yes i know about concurrent dictionary but for the syncing needed in this
        /// class its going to be overkill. just need to protect the linq statements
        /// called so adding or removing doesnt occur whilst they are being processed
        /// </summary>
        private static readonly object _backgroundTasksSync = new object();
        private readonly Dictionary<ISchedulableTask, IObservable<bool>> _backgroundTasks = new Dictionary<ISchedulableTask, IObservable<bool>>();
        private readonly IRxnManager<IRxn> _eventManager;
        private readonly List<IDisposable> _managedResources = new List<IDisposable>();
        private bool _taskHasEncounteredError;

        private DateTime? _groupEndTime;
        private DateTime? _groupStartTime;

        public SchedulableTaskGroup()
        {
            Tasks = new List<ISchedulableTask>();
            Parameters = new List<OutputParameter>();
            _isRunning = new BehaviorSubject<bool>(false);
            _isWaiting = new BehaviorSubject<bool>(false);
            IsEnabled = false;
            Result = new RunningResult();

            DefaultScheduler = NewThreadScheduler.Default;
        }

        /// <summary>
        /// Creates a new instance of the class referencing the configuration for
        /// default values
        /// </summary>
        /// <param name="configuration"></param>
        public SchedulableTaskGroup(IRxnManager<IRxn> eventManager)
            : this()
        {
            _eventManager = eventManager;
        }

        /// <summary>
        /// Runs the groups workflow, executing tasks
        /// </summary>
        public void Execute(ExecutionState initialState = null)
        {
            try
            {
                if (_groupStartTime == null && _eventManager != null && IsReporting)
                {
                    _eventManager.Publish(new SystemStatusMetaEvent()
                    {
                        Meta = () => new
                        {
                            TaskGroup = $"{Name} ({Tasks.Count} - {(_groupStartTime.HasValue ? (_groupEndTime - _groupStartTime).Value.ToString("g") : "0").Replace("0.0", "")})",
                        },
                        ReporterName = Name
                    }).Until();
                }

                _groupStartTime = DateTime.Now;
                _groupEndTime = DateTime.Now;


                _isRunning.SetValue(true);

                OnInformation("Started");

                if (initialState != null)
                {
                    OnVerbose("Inital state found, applying");
                    ResetState(initialState);
                    _state.Group = this;
                }
                else
                {
                    //reset the state to clear any old variables
                    ResetState();
                }

                //execute the tasks
                ExecuteTasks(Tasks);

                //clean up
                _isWaiting.SetValue(true);

                WaitForAllTasksToFinish();

                Result.Name = Name;
                Result.SetAsSuccess(Tasks.Count.ToString());
            }
            catch (Exception e)
            {
                _taskHasEncounteredError = true;
                OnError(e);
                Result.SetAsFailure(e);
            }
            finally
            {
                if (_taskHasEncounteredError)
                {
                    OnWarning("Terminated early due to a task error");
                    Result.SetAsFailure(); ;
                }
                else
                    OnInformation("Finished");

                _isWaiting.SetValue(false);
                _isRunning.SetValue(false);
                _groupEndTime = DateTime.Now;
            }
        }

        public bool RanToCompletion()
        {
            return Result.RanToCompletion();
        }

        private void ExecuteTasks(IEnumerable<ISchedulableTask> tasks)
        {
            foreach (var task in tasks)
            {
                if (_taskHasEncounteredError && task.Conditions.Length() == 0)
                {
                    OnVerbose("Skipping '{0}' because execution conditions do not validate", task.ReporterName);
                    continue;
                }

                //verify execute conditions - this is an OR operation, if any evaluate then the task is run!
                if (task.Conditions.Length() > 0 && !task.Conditions.Any(EvaluateCondition))
                {
                    OnVerbose("Skipping '{0}' because execution conditions do not validate", task.ReporterName);
                    continue;
                }

                //update state
                UpdateTaskStartState();

                WaitForRunningTasksToFinish();

                //execute the task
                if (task.IsSynchronous)
                {
                    RunSync(task);
                }
                else
                {
                    RunAsync(task);
                }
            }
        }

        private void WaitForAllTasksToFinish()
        {
            WaitForRunningTasksToFinish(true);
        }

        /// <summary>
        /// Returns only when the workflow should move to the next state
        /// 
        /// todo: convert these blocking wait()s into non-blocking RX style ContinueWiths()
        /// </summary>
        /// <param name="waitForNonBlockingTasks">Non-blocking tasks are not supposed to block others from executing</param>
        private void WaitForRunningTasksToFinish(bool waitForNonBlockingTasks = false)
        {
            IObservable<bool>[] tasks = null;

            if (_backgroundTasks.Count > 0)
            {
                lock (_backgroundTasksSync)
                {
                    tasks =
                        _backgroundTasks.Where((task, _) => task.Key != null && (task.Key.IsBlocking || waitForNonBlockingTasks))
                            .Select(k => k.Value).ToArray();
                }

                if (tasks.Length > 0)
                    tasks.CombineLatest(a => a.All(isCompleted => isCompleted)) //make sure all are not running
                        .SkipWhile(cond => cond == false).LastOrDefaultAsync().Wait();
            }
        }

        /// <summary>
        /// Runs a task in a new thread, then waits for it finished
        /// </summary>
        /// <param name="task">The task</param>
        private void RunSync(ISchedulableTask task)
        {
            RunAsync(task);

            WaitForRunningTasksToFinish();
        }

        /// <summary>
        /// Starts off a task in a new thread then returns
        /// </summary>
        /// <param name="task">The task</param>
        private void RunAsync(ISchedulableTask task)
        {
            WaitForRunningTasksToFinish();

            OnInformation("Executing task: '{0}'", task.ReporterName);

            var sub = new BehaviorSubject<bool>(false);

            lock (_backgroundTasksSync)
                _backgroundTasks.Add(task, sub);

            var taskErrorSub = task.Errors.Subscribe(_ => SetAsError(task));

            //starts the task
            //dont need to dispose of this subscription because it finishes
            //when the task finishes *i dont think*
            Observable.Start(() => task.Execute(_state), DefaultScheduler)
                      .Subscribe(
                            finalState =>
                            {
                                //only update the state if it "makes sense"
                                //tasks are not allowed to remove variables from the state,
                                //only update their values. if this occours, the state is considered corrupt
                                if (finalState != null && finalState.Variables.Count() >= _state.Variables.Count(c => !c.Key.Equals("TaskFailed")))
                                {
                                    if (_taskHasEncounteredError)
                                        finalState.Variables.AddOrReplace("TaskFailed", _state.Variables["TaskFailed"]);

                                    _state = finalState;
                                }
                                else
                                {
                                    OnWarning("State returned has been corrupted. Reverting to initial state");
                                }

                                UpdateTaskEndState();
                            },
                            error =>
                            {
                                lock (_backgroundTasksSync)
                                    _backgroundTasks.Remove(task);

                                SetAsError(task);
                                OnError(error);

                                //alert subscription
                                sub.SetValue(true);
                                sub.OnCompleted();
                                taskErrorSub.Dispose();
                            },
                            onCompleted: () =>
                            {
                                lock (_backgroundTasksSync)
                                    _backgroundTasks.Remove(task);

                                OnInformation("Finished executing task: {0}", task.ReporterName);

                                //alert subscriptions
                                sub.SetValue(true);
                                sub.OnCompleted();
                                taskErrorSub.Dispose();
                            });
        }

        private void SetAsError(ISchedulableTask task)
        {
            _taskHasEncounteredError = true;
            _state.Variables.AddOrReplace("TaskFailed", task.ReporterName);
        }

        /// <summary>
        /// Returns true if any conditions are met, using the state as a reference
        /// for the conditional values
        /// </summary>
        /// <param name="condition">The condition to verify</param>
        /// <returns>If its verified</returns>
        private bool EvaluateCondition(ExecutionCondition condition)
        {
            var binding = GetBindedValue(condition.Binding);

            if (_taskHasEncounteredError && condition.Binding.Equals("taskfailed", StringComparison.OrdinalIgnoreCase) && condition.Condition != Comparer.Is)
            {
                return false;
            }

            return ExecutionCondition.Evaluate(binding, condition.Value, condition.Condition);
        }

        #region State operations

        private object GetBindedValue(string expression)
        {
            string variableToBind;

            if (!expression.StartsWith("{") || !expression.EndsWith("}"))
                variableToBind = expression;
            else
                variableToBind = expression.Substring(1, expression.Length - 2);

            return _state.Variables.ContainsKey(variableToBind) ? _state.Variables[variableToBind] : null;
        }

        private void UpdateTaskStartState()
        {
            _state.Variables.AddOrReplace("TaskStart", DateTime.Now);
        }

        private void UpdateTaskEndState()
        {
            _state.Variables.AddOrReplace("LastTaskEnd", DateTime.Now);
            _state.Variables.AddOrReplace("LastTaskStart", _state.Variables["TaskStart"]);
        }

        private void ResetState(ExecutionState initalState = null)
        {
            _taskHasEncounteredError = false;

            lock (_backgroundTasksSync)
                _backgroundTasks.Clear();

            _state = initalState ?? new ExecutionState();
            _state.Group = this;
            _state.Variables.AddOrReplace("TaskFailed", false);

            UpdateStartState();
        }

        private void UpdateStartState()
        {
            //add params to state
            foreach (var p in Parameters)
                _state.Variables.AddOrReplace(p.Name, p.Value);

            //add group values
            _state.Variables.AddOrReplace("GroupID", ID);
            _state.Variables.AddOrReplace("GroupName", Name);
            _state.Variables.AddOrReplace("GroupStart", DateTime.Now);
        }

        #endregion


        public ISchedulableTaskGroup ManageDisposalOf(IDisposable resource)
        {
            _managedResources.Add(resource);

            return this;
        }

        private bool _isDiposed = false;

        public override void Dispose()
        {
            if (_isDiposed)
                return;

            _isDiposed = true;
            _managedResources.DisposeAll();

            _isRunning.OnCompleted();
            _isWaiting.OnCompleted();
            _isRunning.Dispose();
            _isWaiting.Dispose();

            Tasks.DisposeAll();

            base.Dispose();
        }
    }
}
