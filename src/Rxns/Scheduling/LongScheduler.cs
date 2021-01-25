using System;
using System.Reactive.Concurrency;

namespace Rxns.Scheduling
{
    /// <summary>
    /// A scheduler that can be used for actions that require a dueTime of longer then Int32.MaxValue
    /// </summary>
    public class LongScheduler : IScheduler//, System.Reactive.IAmReactive
    {
        public IScheduler DefaultScheduler { get; set; }

        public LongScheduler(IScheduler defaultScheduler)
        {
            this.DefaultScheduler = defaultScheduler;
        }

        public DateTimeOffset Now
        {
            get { return DefaultScheduler.Now; }
        }

        public IDisposable Schedule<TState>(TState state, DateTimeOffset dueTime, Func<IScheduler, TState, IDisposable> action)
        {
            return DefaultScheduler.LongSchedule(state, dueTime - Now, action);
        }

        public IDisposable Schedule<TState>(TState state, TimeSpan dueTime, Func<IScheduler, TState, IDisposable> action)
        {

            return DefaultScheduler.LongSchedule(state, dueTime, action);
        }

        public IDisposable Schedule<TState>(TState state, Func<IScheduler, TState, IDisposable> action)
        {
            return DefaultScheduler.Schedule(state, action);
        }
    

}
}
