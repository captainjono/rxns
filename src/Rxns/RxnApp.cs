using System.Reactive.Concurrency;

namespace Rxns
{
    public static class RxnApp
    {
        public static IScheduler UIScheduler = Scheduler.Default;
        public static IScheduler BackgroundScheduler = RxnSchedulers.TaskPool;
    }
}
