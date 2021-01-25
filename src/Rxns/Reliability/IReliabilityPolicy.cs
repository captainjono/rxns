using System;

namespace Rxns.Reliability
{
    public interface IReliabilityPolicy
    {
        TResult Execute<TResult>(Func<TResult> action);
        void Execute(Action action);
    }
}
