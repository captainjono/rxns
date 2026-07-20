using System;

namespace Rxns.Reliability.Retry
{
    internal class RetryPolicyState : IRetryPolicyState
    {
        private readonly Action<Exception, long, Context> _onRetry;
        private readonly Context _context;
        private long _count = 0;

        public RetryPolicyState(Action<Exception, long, Context> onRetry, Context context)
        {
            _onRetry = onRetry;
            _context = context;
        }

        public RetryPolicyState(Action<Exception, long> onRetry) :
            this((exception, count, context) => onRetry(exception, count), null)
        {
        }

        public bool CanRetry(Exception ex)
        {
            _onRetry(ex, _count, _context);
            SafeIncrement(ref _count);
            return true;
        }

        /// <summary>
        /// A hack to make forever retries with a counter possible
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        private void SafeIncrement(ref long value)
        {
            if (value == long.MaxValue)
                value = 1000;
            else
                value++;
        }
    }
}