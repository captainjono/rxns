using System.Reactive.Concurrency;

namespace Rxns
{
    /// <summary>
    /// The types of schedulers that are used within an application domain. each scheduler takes a different role,
    /// and care should be taken on choosing the right scheduler for the task
    /// 
    /// By default, all schedulers hand off to the Rx equivlant scheduler. In the case of constrained decives,
    /// it may be handy to override the functionality of the default scheduler for that platform, and this is the place
    /// to do it.
    /// </summary>
    public static class RxnSchedulers
    {
        /// <summary>
        /// Has to remove this feature due to problems with
        /// </summary>
        public static int ThreadPoolSize
        {
            get { return TaskSchedulerMeta.PoolSize; }
            set { TaskSchedulerMeta = new TaskPoolSchedulerWithLimiter(value); }
        }

        public static TaskPoolSchedulerWithLimiter TaskSchedulerMeta = new TaskPoolSchedulerWithLimiter(8);
        private static IScheduler _taskPoolScheduler;
        /// <summary>
        /// A scheduler which utilising a taskpool for scheduling. 
        /// 
        /// note: previously this was limited by the ThreadPoolSize, but had to remove because of the complex nature of services
        /// which may wait on one of these threads, blocking any more tasks from being scheduled. Happens mostly in UI apps
        /// so the default behaviour has changed.
        /// todo: need to create a limiter for this scheduler to restrict the size of the taskPool using the ThreadPoolSize property
        /// </summary>
        public static IScheduler TaskPool
        {
            get { return _taskPoolScheduler = _taskPoolScheduler ?? TaskPoolScheduler.Default; }
            set { _taskPoolScheduler = value; }
        }

        /// <summary>
        /// WOrk is performed on a new thread, without ever interfearing with the pool
        /// </summary>
        public static IScheduler NewThread
        {
            get { return NewThreadScheduler.Default; }
        }

        /// <summary>
        /// The work is done immediately
        /// </summary>
        public static IScheduler Immediate
        {
            get { return Scheduler.Immediate; }
        }

        /// <summary>
        /// The work is done immediately after the current thread is finished. This is great for recurisive
        /// tail operations to ensure you dont overlfow the stack
        /// </summary>
        public static IScheduler CurrentThread
        {
            get { return CurrentThreadScheduler.Instance; }
        }

        /// <summary>
        /// The default platform scheduler. use this if you dont have a specific use case in mind.
        /// </summary>
        public static IScheduler Default { get { return Scheduler.Default; }}
    }
}
