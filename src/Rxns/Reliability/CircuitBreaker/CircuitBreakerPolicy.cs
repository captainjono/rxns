using System;
using System.Collections.Generic;
using System.Linq;
using Rxns.Interfaces.Reliability;
using Rxns.Reliability;

namespace Rxns.Reliability.CircuitBreaker
{
    internal class CircuitBreakerPolicy
    {
        internal static void Implementation(Action action, IEnumerable<ExceptionPredicate> shouldRetryPredicates, ICircuitBreakerState breakerState)
        {
            if (breakerState.IsBroken)
            {
                throw new BrokenCircuitException("The circuit is now open and is not allowing calls.", breakerState.LastException);
            }

            try
            {
                action();
                breakerState.Reset();
            }
            catch (Exception ex)
            {
                if (!shouldRetryPredicates.Any(predicate => predicate(ex)))
                {
                    throw;
                }

                breakerState.TryBreak(ex);

                throw;
            }
        }
    }
}