using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Rxns.Logging;
using Rxns.Interfaces.Reliability;
using Unit = System.Reactive.Unit;

namespace Rxns.Reliability
{
    public interface IReliabilityManagerCfg
    {
        bool IsEnabled { get; }
        int RetryCount { get; }
    }

    public class RetryMaxTimesReliabilityCfg : IReliabilityManagerCfg
    {
        public bool IsEnabled => true;
        public int RetryCount { get; private set; }

        public RetryMaxTimesReliabilityCfg(int retryCount)
        {
            RetryCount = retryCount;
        }
    }

    /// <summary>
    /// The reliability manager is used to to make connections to extenal sources. The manager will automatically
    /// retry the connection attempt if an exception is received that it deems as "transient".
    /// 
    /// Convience methods are availible to chain to the result of "call" functions.
    /// - OnSuccess() 
    /// - OnFailure() 
    /// - OnlyIfSuccessful()
    /// 
    /// Note: not all exceptions are transient! In these cases, the system will not retry at all!
    /// </summary>
    public class ReliabilityManager : ReportsStatus, IReliabilityManager
    {
        public static List<TimeSpan> BackOffSchedule;
        public static PolicyBuilder HttpStratergy;
        public static PolicyBuilder SqlStratery;
        public static PolicyBuilder AnyErrorStratergy;

        public IScheduler DefaultScheduler { get; set; }

        public bool IsEnabled { get; set; }
        public int RetryCount { get; set; }

        public ReliabilityManager(IReliabilityManagerCfg cfg, IScheduler scheduler = null)
        {
            DefaultScheduler = scheduler ?? TaskPoolScheduler.Default;
            BackOffSchedule = GetBackoffSchedule();

            SetConfiguration(cfg);
            SetupPolicies();
        }

        private void SetConfiguration(IReliabilityManagerCfg cfg)
        {
            IsEnabled = cfg.IsEnabled;
            RetryCount = cfg.RetryCount;
        }

        private void SetupPolicies()
        {
            AnyErrorStratergy = Policy
                                    .Handle<Exception>();

            HttpStratergy = Policy
                                .Handle<TimeoutException>()
                                .Or<HttpException>(ex => IsTransient(ex.StatusCode));

            SqlStratery = HttpStratergy = Policy
                                .Handle<TimeoutException>();
        }

        private List<TimeSpan> GetBackoffSchedule()
        {
            var schedule = new List<TimeSpan>(new[]
            {
                TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(60),
                TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30)
            });

            return schedule;
        }

        private TimeSpan GetBackoffForCount(long count)
        {
            return count >= BackOffSchedule.Count ? BackOffSchedule.Last() : BackOffSchedule[(int)count];
        }
        
        private void DoRetry(string stratergy, Exception exception, long retryCount)
        {
            var backOff = GetBackoffForCount(retryCount);
            
            OnVerbose("{0}: Attempt failed with '{1}' retry '#{2}' in '{3}'", stratergy, exception.GetBaseException().Message, retryCount, backOff);

            SystemClock.Sleep(backOff);
        }

        /// <summary>
        /// This method determines if a status code should be 
        /// considered a condition that the system will automatically recover from
        /// </summary>
        /// <param name="statusCode">The status code to examine</param>
        /// <returns></returns>
        public bool IsTransient(HttpStatusCode statusCode)
        {
            //any server error, caused by a db fault, outage, etc
            //could be considered transient
            if ((int)statusCode >= 500)
                return true;

            switch (statusCode)
            { 
                case HttpStatusCode.Unauthorized:
                    return true;
            }

            return false;
        }

        //public IObservable<T> CallOverHttp<T>(Func<IObservable<T>> action, IScheduler scheduler)
        //{
        //    return CallWithPolicy(action, AnyErrorStratergy, DefaultScheduler);
        //}

        /// <summary>
        /// Performs a task using the HttpStratery with default system retry policy
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="action">The task that uses http as a transport</param>
        /// <param name="scheduler">The scheduler to runt the action on</param>
        /// <returns>A sequence that produces a single value, the result of the task. if the value is null, the task failed and was aborted.</returns>
        public IObservable<T> CallOverHttp<T>(Func<Task<T>> action, IScheduler scheduler = null)
        {
            var policy = HttpStratergy.Retry(RetryCount, (exception, count) => DoRetry("HttpRetry", exception, count));

            return CallWithPolicy(async () => await action.ThrowExceptions(), policy, scheduler ?? DefaultScheduler).SelectMany(tsk => tsk);;
        }

        //public IObservable<T> CallOverHttp<T>(Func<IObservable<T>> action, IScheduler scheduler)
        //{
        //    var policy = HttpStratergy.Retry(RetryCount, (exception, count) => DoRetry("HttpRetry", exception, count));

        //    return CallWithPolicy(() => action.ThrowExceptions(), policy, scheduler); ;
        //}

        public IObservable<T> CallOverHttpForever<T>(Func<Task<T>> action)
        {
            return CallOverHttpForever(action, DefaultScheduler);
        }

        /// <summary>
        /// Performs a task using the HttpStratery and retries forever using the
        /// BackOffSchedule defined in this class
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="action">The task that uses http as a transport</param>
        /// <param name="scheduler">The scheduler to run the work on</param>
        /// <param name="reporter">The reporter to use for any logging</param>
        /// <returns>A sequence that produces a single value, the result of the task. Null signifies a task that failed.</returns>
        public IObservable<T> CallOverHttpForever<T>(Func<Task<T>> action, IScheduler scheduler)
        {
            var policy = AnyErrorStratergy.RetryForever((exception, count) => DoRetry("HttpForever", exception, count));

            return CallWithPolicy(async () => await action.ThrowExceptions(), policy, scheduler ?? DefaultScheduler).SelectMany(tsk => tsk); ;
        }

        public IObservable<T> CallDatabase<T>(Func<T> action, IScheduler scheduler = null)
        {
            var policy = SqlStratery.Retry(RetryCount, (exception, count) => DoRetry("SqlRetry", exception, count));

            return CallWithPolicy(action, policy, scheduler ?? DefaultScheduler);
        }

        /// <summary>
        /// Reliably executes a function, using the reliability policy supplied
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="action">The function to execute</param>
        /// <param name="retryPolicy">The retry policy to use when executing the function</param>
        /// <param name="scheduler">The scheduler to run the work on</param>
        /// <returns>A sequence that produces a single value, the result of the task. If null, the task failed</returns>
        public IObservable<T> CallWithPolicy<T>(Func<T> action, IReliabilityPolicy retryPolicy, IScheduler scheduler = null)
        {
            return IsEnabled ? Observable.Start(() => retryPolicy.Execute(action), scheduler ?? DefaultScheduler) : Observable.Start(action, scheduler ?? DefaultScheduler);
        }

        public IObservable<T> CallWithPolicy<T>(Func<IObservable<T>> action, Action<Exception> onRetry = null, IScheduler scheduler = null)
        {
            return Observable.Create<T>(o =>
            {
                Func<Func<IObservable<T>>, int, IScheduler, IDisposable> recursive = (_, __, ___) => Disposable.Empty;

                recursive = (operation, count, sched) =>
                {
                    return operation().Subscribe(success =>
                    {
                        o.OnNext(success);
                        o.OnCompleted();
                    },
                    ex =>
                    {
                        try
                        {
                            if (count >= RetryCount)
                            {
                                o.OnError(ex);
                            }
                            else
                            {
                                var backOff = GetBackoffForCount(count);
                                OnVerbose("CallWithpolicy: Attempt failed with '{0}' retry '#{1}' in '{2}'", ex.Message, count, backOff);
                                if (onRetry != null) onRetry(ex); 
                                Observable.Timer(BackOffSchedule[count++], sched).Subscribe(s => recursive(operation, count, sched));
                            }
                        }
                        catch (Exception e)
                        {
                            o.OnError(e);
                        }
                    });
                };

                return recursive(action, 0, scheduler ?? DefaultScheduler);
            });
        }

        public IObservable<Unit> CallWithPolicy(Action action, IReliabilityPolicy retryPolicy, IScheduler scheduler = null)
        {
            return IsEnabled ? Observable.Start(() => retryPolicy.Execute(action), scheduler) : Observable.Start(action, scheduler ?? DefaultScheduler);
        }

        public IObservable<T> CallWithPolicy<T>(Func<T> action, PolicyBuilder retryPolicy, IScheduler scheduler = null)
        {
            return CallWithPolicy(action, retryPolicy.Retry(RetryCount, (exception, count) => DoRetry("CallWithPolicy", exception, count)), scheduler ?? DefaultScheduler);
        }

        public IObservable<T> CallWithPolicyForever<T>(Func<T> action, PolicyBuilder retryPolicy, IScheduler scheduler = null)
        {
            return CallWithPolicy(action, retryPolicy.RetryForever((exception, count) => DoRetry("CallWithPolicyForever", exception, count)), scheduler ?? DefaultScheduler);
        }

        public IObservable<Unit> CallWithPolicy(Action action, PolicyBuilder retryPolicy, IScheduler scheduler = null)
        {
            return CallWithPolicy(action, retryPolicy.Retry(RetryCount, (exception, count) => DoRetry("CallWithPolicy", exception, count)), scheduler ?? DefaultScheduler);
        }
        
        public IObservable<Unit> CallDatabase(Action action, IScheduler scheduler = null)
        {
            var policy = SqlStratery.Retry(RetryCount, (exception, count) => DoRetry("sqlRetry", exception, count));

            return CallWithPolicy(action, policy, scheduler ?? DefaultScheduler);
        }
    }

}
