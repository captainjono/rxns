using System;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Threading.Tasks;
using Rxns.Reliability;

namespace Rxns.Interfaces.Reliability
{
    public interface IReliabilityManager
    {
        /// <summary>
        /// Calls a database with the default SQL Server retry policy
        /// </summary>
        /// <typeparam name="T">The result of the database operation</typeparam>
        /// <param name="action"></param>
        /// <param name="scheduler">The scheduler to execute the action on</param>
        /// <returns></returns>
        IObservable<T> CallDatabase<T>(Func<T> action, IScheduler scheduler = null);
        
        IObservable<Unit> CallDatabase(Action action, IScheduler scheduler = null);

        /// <summary>
        /// Performs a task using the HttpStratery with default system retry policy
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="action">The task that uses http as a transport</param>
        /// <param name="scheduler">The scheduler to execute the action on</param>
        /// <returns>A sequence that produces a single value, the result of the task. if the value is null, the task failed and was aborted.</returns>
        IObservable<T> CallOverHttp<T>(Func<Task<T>> action, IScheduler scheduler = null);
        
        /// <summary>
        /// Performs a task using the HttpStratery and retries forever using the
        /// BackOffSchedule defined in this class
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="action">The task that uses http as a transport</param>
        /// <param name="scheduler">The scheduler to execute the action on</param>
        /// <returns>A sequence that produces a single value, the result of the task. Null signifies a task that failed.</returns>

        IObservable<T> CallOverHttpForever<T>(Func<Task<T>> action, IScheduler scheduler = null);
        /// <summary>
        /// Reliably executes a function, using the reliability policy supplied
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="action">The function to execute</param>
        /// <param name="retryPolicy">The retry policy to use when executing the function</param>
        /// <param name="scheduler">The scheduler to execute the action on</param>
        /// <returns>A sequence that produces a single value, the result of the task. If null, the task failed</returns>
        IObservable<T> CallWithPolicy<T>(Func<T> action, IReliabilityPolicy retryPolicy, IScheduler scheduler = null);

        /// <summary>
        /// Reliably executes an action, using the reliability policy supplied
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="action">The function to execute</param>
        /// <param name="retryPolicy">The retry policy to use when executing the function</param>
        /// <param name="scheduler">The scheduler to execute the action on</param>
        /// <returns>A sequence that produces a single value, the result of the task. If null, the task failed</returns>
        IObservable<Unit> CallWithPolicy(Action action, IReliabilityPolicy retryPolicy, IScheduler scheduler = null);

        /// <summary>
        /// A fully async implementing of a retry operation that retries whenever any exception is encountered
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="action">The action toe execute</param>
        /// <param name="scheduler">The scheduler used to operate the retry delay timer</param>
        /// <returns></returns>
        IObservable<T> CallWithPolicy<T>(Func<IObservable<T>> action, Action<Exception> onRetry = null, IScheduler scheduler = null);
        /// <summary>
        /// Reliably executes a function, retrying forver
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="action"></param>
        /// <param name="retryPolicy"></param>
        /// <param name="scheduler">The scheduler to execute the action on</param>
        /// <returns></returns>
        IObservable<T> CallWithPolicyForever<T>(Func<T> action, PolicyBuilder retryPolicy, IScheduler scheduler = null);
        IObservable<Unit> CallWithPolicy(Action action, PolicyBuilder retryPolicy, IScheduler scheduler = null);
        IObservable<T> CallWithPolicy<T>(Func<T> action, PolicyBuilder retryPolicy, IScheduler scheduler = null);
    }
}