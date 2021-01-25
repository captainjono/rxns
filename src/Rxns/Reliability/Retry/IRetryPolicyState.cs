using System;

namespace Rxns.Reliability.Retry
{
    internal interface IRetryPolicyState
    {
        bool CanRetry(Exception ex);
    }
}