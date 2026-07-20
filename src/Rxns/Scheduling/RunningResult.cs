using System;

namespace Rxns.Scheduling
{
    /// <summary>
    /// Represents the result of a task that has been executed
    /// If the result is successful, the value should be set to 
    /// what is considered the output of the task. If its unsuccessful, the
    /// error property can be set with the exception that occoured, but this is
    /// required
    /// 
    /// ie. the amount of records effected, files deleted
    /// </summary>
    public class RunningResult
    {
        public string Name { get; set; }
        public object Value { get; set; }
        public Exception Error { get; set; }

        public bool RanToCompletion()
        {
            return Value.GetType() != typeof(RunningResults) || ((RunningResults)Value) != RunningResults.Failed;
        }

        public void SetAsSuccess(object value)
        {
            Value = value;
        }

        public void SetAsFailure()
        {
            Value = RunningResults.Failed;
        }
        public void SetAsFailure(Exception e)
        {
            Error = e;
            SetAsFailure();
        }
    }

    public enum RunningResults
    {
        Failed = -1,
        Success = 0
    }
}
