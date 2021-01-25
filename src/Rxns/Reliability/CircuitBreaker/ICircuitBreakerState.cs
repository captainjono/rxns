using System;

namespace Rxns.Reliability.CircuitBreaker
{
    internal interface ICircuitBreakerState
    {
        Exception LastException { get; }
        bool IsBroken { get; }
        void Reset();
        void TryBreak(Exception ex);
    }
}