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
        public string Body;

        public HttpException(HttpResponseMessage response) : this(response, ReadBody(response)) { }

        private HttpException(HttpResponseMessage response, string body) : base(BuildMessage(response, body))
        {
            StatusCode = response.StatusCode;
            Response = response;
            Body = body;
        }

        /// <summary>
        /// Build an HttpException when the body has already been read by the caller —
        /// the right path when the response will be re-inspected by code that triggers
        /// content disposal (e.g. EnsureSuccessStatusCode in modern .NET).
        /// </summary>
        public static HttpException FromResponseAndBody(HttpResponseMessage response, string body)
            => new HttpException(response, body);

        // Reads the response body synchronously. The body is the only place ASP.NET
        // Core's InternalServerError(e) emits the actual exception text, so it's
        // what callers (and the support portal adapter trace) need to diagnose
        // remote failures. Capped to keep the exception message bounded.
        public static string ReadBody(HttpResponseMessage message)
        {
            const int MaxBodyChars = 2048;
            try
            {
                if (message?.Content == null) return null;
                var raw = message.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if (string.IsNullOrEmpty(raw)) return null;
                return raw.Length > MaxBodyChars
                    ? raw.Substring(0, MaxBodyChars) + "...[+" + (raw.Length - MaxBodyChars) + " more]"
                    : raw;
            }
            catch (Exception bodyEx)
            {
                return "<failed to read body: " + bodyEx.GetType().Name + ": " + bodyEx.Message + ">";
            }
        }

        private static string BuildMessage(HttpResponseMessage message, string body)
        {
            var statusLine = String.Format("[{0}] {1} : {2}", (int)message.StatusCode, message.RequestMessage.RequestUri, message.ReasonPhrase);
            return body == null ? statusLine : statusLine + " body=" + body;
        }

        // Back-compat — callers using the older single-arg form still get a status-only message.
        public static string AsMessage(HttpResponseMessage message) => BuildMessage(message, ReadBody(message));
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

                // Don't call EnsureSuccessStatusCode — in modern .NET it reads-and-disposes
                // the response content into its OWN exception message, leaving nothing for
                // us to capture into HttpException.Body. Check status manually and read the
                // body ourselves while the StreamContent is still alive.
                if (!httpResult.IsSuccessStatusCode)
                {
                    var body = HttpException.ReadBody(httpResult);
                    Debug.WriteLine("[{0}]{1}", httpResult.StatusCode, httpResult.ReasonPhrase);
                    throw HttpException.FromResponseAndBody(httpResult, body);
                }
            }
        }
    }
}
