using System;
using System.Collections.Generic;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Rxns.Logging;

namespace Rxns.Scheduling
{
    /// <summary>
    /// This class represents a piece of work that can be orchestrated by a Schedulablegroup. 
    /// </summary>
    public abstract partial class SchedulableTask : ReportsStatus, ISchedulableTask, IPlugableTask
    {
        /// <summary>
        /// The number of items effected by this tasks execution
        /// </summary>
        public RunningResult Result { get; set; }

        //state variables
        private readonly BehaviorSubject<bool> _isRunning = new BehaviorSubject<bool>(false);
        public IObservable<bool> IsRunning { get { return _isRunning; } }

        private readonly BehaviorSubject<bool> _isWaiting = new BehaviorSubject<bool>(false);
        public IObservable<bool> IsWaiting { get { return _isWaiting; } }
        
        /// <summary>
        /// Sets up the class with default values
        /// </summary>
        protected SchedulableTask()
        {
            InputParameters = new List<InputParameter>();
            OutputParameters = new List<OutputParameter>();
            Conditions = new List<ExecutionCondition>();
            IsSynchronous = true;
            IsBlocking = true;
            Result = new RunningResult();
        }

        /// <summary>
        /// Executes the stored procedure
        /// 
        /// todo: convert wait() into ContinueWith() and return IObservable<ExecutionState>()
        /// </summary>
        public ExecutionState Execute(ExecutionState state)
        {
            try
            {
                Result.Name = ReporterName;
                Result.Value = 0;

                _isRunning.OnNext(true);

                OnVerbose("Binding task variables to state");

                BindToState(state);

                OnInformation("Executing");

                var task = ExecuteTaskAsync(state);

                task.Wait();
                
                return AddReturnValue(task.Result);
            }
            catch (Exception ex)
            {
                Result.Value = RunningResults.Failed;
                OnError(ex);
            }
            finally
            {
                OnInformation("Finished");
                _isRunning.OnNext(false);
            }
            
            return state;
        }
        
        private ExecutionState AddReturnValue(ExecutionState executionState)
        {
            executionState.Variables.AddOrReplace(String.Format("__return_{0}", Result.Name), Result.Value);

            return executionState;
        }

        /// <summary>
        /// Implements the work executed by this task
        /// 
        /// Note: the state is not allowed to remove elements from the state, only add to it.
        /// If it returns a state that doesnt comply, the state will be rejected by the system
        /// and revert to the initial version before the task took hold of it
        /// </summary>
        /// <param name="state">The state as supplied by the group</param>
        /// <returns>The updated state as appended by the task</returns>
        public abstract Task<ExecutionState> ExecuteTaskAsync(ExecutionState state);

        /// <summary>
        /// Updates the state with the supplied output parameter values. does not "add"
        /// to state, only updates existing values
        /// </summary>
        /// <param name="outputParameters">The new values</param>
        /// <param name="state">The state to update</param>
        /// <returns>The updated state</returns>
        protected ExecutionState UpdateOutputParameter(IEnumerable<OutputParameter> outputParameters, ExecutionState state)
        {
            foreach (var output in outputParameters)
                state.Variables.AddOrReplace(output.Name, output.Value);

            return state;
        }

        /// <summary>
        /// updates any input bindings with
        /// values found in the state. This will throw an exception if the
        /// state variables referenced by input bindings dont exist
        /// </summary>
        /// <param name="state"></param>
        protected virtual void BindToState(ExecutionState state)
        {
            //now bind input params
            foreach (var input in InputParameters)
                if (!String.IsNullOrEmpty(input.Binding))
                    input.Value = BindToState(input.Binding, state);

            //state.Variables.AddOrReplace("Timestamp", DateTime.Now);
        }

        /// <summary>
        /// Looks up the state for a matching binding, then returns the value
        /// the binding provides
        /// </summary>
        /// <param name="binding">The '{binding}' to query, non-braced bindings are considered values are returned as the value</param>
        /// <param name="state">The state to query for the binding</param>
        /// <param name="defaultValue"></param>
        /// <returns>The value provided by the binding</returns>
        protected object BindToState(object binding, ExecutionState state, object defaultValue = null)
        {
            //do we have a null binding binding?
            if (binding == null)
                return null;

            var toMatch = binding.ToString();

            //is a binding specified?
            if (!toMatch.IsBinding())
                return binding;

            var variableToBind = toMatch.Substring(1, toMatch.Length - 2);

            if (!state.Variables.ContainsKey(variableToBind))
                if (defaultValue != null)
                    return defaultValue;
                else
                    throw new Exception(String.Format("Unable to find specified binding '{0}'", binding));

            if (variableToBind == "Timestamp")
                return DateTime.Now;

            return state.Variables[variableToBind];
        }

        /// <summary>
        /// Completes any observables provided by this class to avoid
        /// memory leaks
        /// </summary>
        public override void Dispose()
        {
            if (!IsDisposed)
            {
                _isRunning.OnCompleted();
                _isRunning.Dispose();

                base.Dispose();
            }
        }

    }

    public static class StringExtensions
    {
        public static bool IsBinding(this string str, string startToken = "{", string endToken = "}")
        {
            return !String.IsNullOrWhiteSpace(str) && str.StartsWith(startToken) && str.EndsWith(endToken);
        }
    }
}