using System;
using System.Threading;

#if PORTABLE
using System.Threading.Tasks;
#endif

namespace Rxns.Reliability.Utilities
{
    /// <summary>
    /// Time related delegates used to improve testability of the code
    /// </summary>
    public static class SystemClock
    {
        /// <summary>
        /// Allows the setting of a custom Thread.Sleep implementation for testing.
        /// By default this will be a call to <see cref="Thread.Sleep(TimeSpan)"/>
        /// </summary>
#if !PORTABLE
        public static Action<TimeSpan> Sleep = (ts) => new ManualResetEvent(false).WaitOne(ts);
#endif
#if PORTABLE
        public static Action<TimeSpan> Sleep = async span => await Task.Delay(span);
#endif
        /// <summary>
        /// Allows the setting of a custom DateTime.UtcNow implementation for testing.
        /// By default this will be a call to <see cref="DateTime.UtcNow"/>
        /// </summary>
        public static Func<DateTime> UtcNow = () => DateTime.UtcNow;
        /// <summary>
        /// Allows the setting of a custom DateTime.Now implementation for testing.
        /// By default this will be a call to <see cref="DateTime.Now"/>
        /// </summary>
        public static Func<DateTime> Now = () => DateTime.Now;


        public static Func<DateTimeOffset> UtcNowOff = () => DateTimeOffset.UtcNow;

        public static Func<DateTimeOffset> NowOff = () => DateTimeOffset.UtcNow;
        /// <summary>
        /// Resets the custom implementations to their defaults. 
        /// Should be called during test teardowns.
        /// </summary>
        public static void Reset()
        {
#if !PORTABLE
            Sleep = (ts) => new ManualResetEvent(false).WaitOne(ts); ;
#endif
#if PORTABLE
            Sleep = async span => await Task.Delay(span);
#endif
            UtcNow = () => DateTime.UtcNow;

            Now = () => DateTime.Now;
        }
    }
}