using System;
using System.Reactive.Concurrency;

namespace Rxns.Scheduling
{
    public static class SchedulerExtensions
    {
        private static TimeSpan _maxDueTime = TimeSpan.FromMilliseconds(Int32.MaxValue);

        /// <summary>
        /// Due to framework factors, it is not possible to schedule actions greater then Int32.MaxValue. As such
        /// this method uses Trampoline recursion to forward schedule these actions at intervals of Int32.MaxValue
        /// until the dueTime is fully reached, then executes the action.
        /// </summary>
        /// <typeparam name="TState">The state type</typeparam>
        /// <param name="scheduler">The scheduler</param>
        /// <param name="state">The state</param>
        /// <param name="dueTime">The time to run the action</param>
        /// <param name="action">The action to run</param>
        /// <returns>A disposable to cancel the schedule</returns>
        public static IDisposable LongSchedule<TState>(this IScheduler scheduler, TState state, TimeSpan dueTime, Func<IScheduler, TState, IDisposable> action)
        {
            //find out how many times we need to resurcuse
            double intervals = Math.Floor(dueTime.TotalMilliseconds / Int32.MaxValue);

            //can we schedule the action normally?
            if (intervals < 1)
            {
                return scheduler.Schedule(state, dueTime, action);
            }
            else
            { 
                bool waitedLastInterval = false;

                //this is the trampoline, using a function to execute the action
                //means only a 2 stack frames are used
                return scheduler.Schedule(_maxDueTime, self =>
                {
                    if (!waitedLastInterval)
                    {
                        intervals--;

                        if (intervals >= 1)
                        {
                            self(_maxDueTime);
                        }
                        else
                        {
                            waitedLastInterval = true;

                            double lastInterval = dueTime.TotalMilliseconds % Int32.MaxValue;

                            self(TimeSpan.FromMilliseconds(lastInterval));
                        }
                    }
                    else
                        action(scheduler, state);
                });
            }
        }
    }
}
