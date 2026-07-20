using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Linq;
using Newtonsoft.Json.Serialization;
using Rxns.Interfaces.Reliability;
using Rxns.Reliability;

namespace Rxns.Reliability.Retry
{
    internal static class RetryPolicy
    {
        public static void Implementation(Action action, IEnumerable<ExceptionPredicate> shouldRetryPredicates, Func<IRetryPolicyState> policyStateFactory)
        {
            var policyState = policyStateFactory();

            while (true)
            {
                try
                {
                    action();
                    return;
                }
                catch (Exception ex)
                {
                    ExecutePredicate(ex.GetInnerMostException(), shouldRetryPredicates, policyState);
                }
            }
        }

        public static IObservable<T> ObservableImplementation<T>(Func<IObservable<T>> action,
            IEnumerable<ExceptionPredicate> shouldRetryPredicates, Func<IRetryPolicyState> policyStateFactory)
        {
            return Observable.Create<T>(o =>
            {
                var policyState = policyStateFactory();
                Func<IDisposable> attempt = null;
                attempt = () =>
                {

                    return action().Subscribe(success =>
                    {
                        o.OnNext(success);
                        o.OnCompleted();
                    },
                    ex =>
                    {

                        try
                        {
                            ExecutePredicate(ex.GetInnerMostException(), shouldRetryPredicates, policyState);
                            attempt();
                        }
                        catch (Exception e)
                        {
                            o.OnError(e);
                        }   
                    });
                };

                return attempt();
            });
        }

        [DebuggerStepThrough]
        public static void ExecutePredicate(Exception ex, IEnumerable<ExceptionPredicate> shouldRetryPredicates, IRetryPolicyState policyState)
        {
            if (!shouldRetryPredicates.Any(predicate => predicate(ex)))
            {
                throw ex;
            }

            if (!policyState.CanRetry(ex))
            {
                throw ex;
            }
        }

    }
}