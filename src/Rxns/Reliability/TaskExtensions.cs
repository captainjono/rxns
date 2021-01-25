using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rxns.Reliability
{
    /// <summary>
    /// .net4 does not have a proper way to bubble up http exceptions with status
    /// codes, other then parsing strings. this class fills the gap.
    /// </summary>
    public class HttpException : Exception
    {
        public HttpStatusCode StatusCode;
        public HttpResponseMessage Response;

        public HttpException(HttpResponseMessage response) : base(AsMessage(response))
        {
            StatusCode = response.StatusCode;
            Response = response;
        }

        public static string AsMessage(HttpResponseMessage message)
        {
            return String.Format("[{0}] {1} : {2}", (int)message.StatusCode, message.RequestMessage.RequestUri, message.ReasonPhrase);
        }
    }

    /// <summary>
    /// Extension methods that complement the reliability manager
    /// </summary>
    public static class TaskExtensions
    {
        /// <summary>
        /// A function that recursively looks through an exception for the one
        /// that has a null innerexception. This is the REAL GetBaseException();
        /// </summary>
        /// <param name="exception"></param>
        /// <returns></returns>
        public static Exception GetInnerMostException(this Exception exception)
        {
            if (exception.InnerException != null)
                return GetInnerMostException(exception.InnerException);

            return exception;
        }

        /// <summary>
        /// Performs a task synchronously and throws any exceptions that may result
        /// Understands the following types of tasks:
        /// Task:: generic
        /// Task:: HttpResponseMessage
        /// 
        /// And will surface exceptions captured by them
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="task">The task to run synchronously</param>
        /// <returns>The task which completed successfully. An exception if not.</returns>
        public static async Task<T> ThrowExceptions<T>(this Func<Task<T>> task)
        {
            var todo = task();
            var result = await todo;
            todo.ThrowExceptions();

            return result;
        }

        public static IObservable<T> ThrowExceptions<T>(this Func<IObservable<T>> task)
        {
            return task();
        }

        /// <summary>
        /// Performs a task synchronously and throws any exceptions that may result
        /// Understands the following types of tasks:
        /// Task:: generic
        /// Task:: HttpResponseMessage
        /// 
        /// And will surface exceptions captured by them
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="task">The task to run synchronously</param>
        /// <returns>The task which completed successfully. An exception if not.</returns>
        public static void ThrowExceptions<T>(this Task<T> task)
        {
            if (task.IsFaulted && task.Exception != null)
                throw task.Exception;

            if (task.Result is HttpResponseMessage)
            {
                var httpResult = task.Result as HttpResponseMessage;
                try
                {
                    httpResult.EnsureSuccessStatusCode();
                }
                catch (Exception e)
                {
                    Debug.WriteLine("[{0}]{1}", httpResult.StatusCode, httpResult.ReasonPhrase);
                    throw new HttpException(httpResult);
                }
            }
        }
    }
}
