using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Rxns;
using Rxns.Interfaces;
using Rxns.Logging;
using Rxns.Metrics;

namespace System.Reactive.Linq
{
    /// <summary>
    /// Some handy fluent orientated extesions for IReportStatus
    /// </summary>
    public static class IReportStatusExtensions
    {
        /// <summary>
        /// A handy way to find out all the info about your reporter in one call
        /// </summary>
        /// <param name="reporter"></param>
        /// <param name="information">What todo when information happens</param>
        /// <param name="errors">The  todo when an error happens</param>
        /// <returns>A subscription which u can dispose of to cancel your handler</returns>
        public static CompositeDisposable SubscribeAll(this IReportStatus reporter, Action<LogMessage<string>> information = null, Action<LogMessage<Exception>> errors = null)
        {
            var subs = new CompositeDisposable();

            var errorStream = reporter?.Errors;
            var infoStream = reporter?.Information;

            if (errors != null)
                errorStream?.Do(e => errors(e)).Until().DisposedBy(subs);

            if (information != null)
                infoStream?.Do(e => information(e)).Until().DisposedBy(subs);

            return subs;
        }

        public static CompositeDisposable ReportToDebug(this IReportStatus reporter)
        {
            if(Debugger.IsAttached)
                return reporter.SubscribeAll(i => Debug.WriteLine(i), e => Debug.WriteLine(e));
            else
                return reporter.SubscribeAll(i => Console.WriteLine(i), e => Console.WriteLine(e));
        }

        public static CompositeDisposable ReportToConsole(this IReportStatus reporter)
        {
            return reporter.SubscribeAll(i => Console.WriteLine(i), e => Console.WriteLine(e));
        }

        public static CompositeDisposable SubscribeAll(this IEnumerable<IReportStatus> reporters, Action<LogMessage<string>> information = null, Action<LogMessage<Exception>> errors = null)
        {
            var subs = new CompositeDisposable();

            IObservable<LogMessage<Exception>> errorStream = null;
            IObservable<LogMessage<string>> infoStream = null;

            foreach (var reporter in reporters)
            {
                if (errorStream == null)
                {
                    errorStream = reporter.Errors;
                    infoStream = reporter.Information;
                }
                else
                {
                    errorStream = errorStream.Merge(reporter.Errors);
                    infoStream = infoStream.Merge(reporter.Information);
                }
            }

            if (errors != null)
                subs.Add(errorStream.Subscribe(errors));

            if (information != null)
                subs.Add(infoStream.Subscribe(information));

            return subs;
        }

        /// <summary>
        /// Reports any exceptions that occour through your reporter
        /// </summary>
        /// <param name="reporter">The person who will catch the exception</param>
        /// <param name="work">The work todo</param>
        public static void TryCatch(this IReportStatus reporter, Action work)
        {
            try
            {
                work.Invoke();
            }
            catch (Exception e)
            {
                reporter.OnError(e);
            }
        }

        /// <summary>
        /// Reports any exceptions that occour through your reporter
        /// </summary>
        /// <param name="reporter">The person who will catch the exception</param>
        /// <param name="work">The work todo</param>
        public static dynamic TryCatch(this IReportStatus reporter, Func<dynamic> work)
        {
            try
            {
                return work.Invoke();
            }
            catch (Exception e)
            {
                reporter.OnError(e);
                return null;
            }
        }

        /// <summary>
        /// Reports any exceptions that occour through your reporter
        /// </summary>
        /// <param name="reporter">The person who will catch the exception</param>
        /// <param name="work">The work todo</param>
        public static T TryCatch<T>(this IReportStatus reporter, Func<T> work)
        {
            try
            {
                return work.Invoke();
            }
            catch (Exception e)
            {
                reporter.OnError(e);
                return default(T);
            }
        }

        /// <summary>
        /// Reports any exceptions that occour through your reporter
        /// </summary>
        /// <param name="reporter">The person who will catch the exception</param>
        /// <param name="work">The work todo</param>
        /// <param name="onException">A handler that will be called when an exception occours, after it is logged by the reporter</param>
        public static void ReportExceptions(this IReportStatus reporter, Action work, Action<Exception> onException = null)
        {
            try
            {
                work.Invoke();
            }
            catch (Exception e)
            {
                reporter.OnError(e);

                if (onException != null)
                    onException.Invoke(e);
            }
        }

        /// <summary>
        /// Same are trycatch
        /// Reports any exceptions that occour through your reporter
        /// </summary>
        /// <param name="reporter">The person who will catch the exception</param>
        /// <param name="work">The work todo</param>
        public static dynamic ReportExceptions(this IReportStatus reporter, Func<dynamic> work)
        {
            try
            {
                return work.Invoke();
            }
            catch (Exception e)
            {
                reporter.OnError(e);
                return null;
            }
        }

        /// <summary>
        /// Same are trycatch
        /// Reports any exceptions that occour through your reporter
        /// </summary>
        /// <param name="reporter">The person who will catch the exception</param>
        /// <param name="work">The work todo</param>
        public static T ReportExceptions<T>(this IReportStatus reporter, Func<T> work)
        {
            try
            {
                return work.Invoke();
            }
            catch (Exception e)
            {
                reporter.OnError(e);
                return default(T);
            }
        }

        /// <summary>
        /// Same are trycatch
        /// Reports any exceptions that occour through your reporter
        /// </summary>
        /// <param name="reporter">The person who will catch the exception</param>
        /// <param name="work">The work todo</param>
        /// <param name="onException">A handler that will be called when an exception occours, after it is logged by the reporter</param>
        public static dynamic ReportExceptions(this IReportStatus reporter, Func<dynamic> work, Action<Exception> onException = null)
        {
            try
            {
                return work.Invoke();
            }
            catch (Exception e)
            {
                reporter.OnError(e);

                if (onException != null)
                    onException.Invoke(e);
                return null;
            }
        }

        /// <summary>
        /// Same are trycatch
        /// Reports any exceptions that occour through your reporter
        /// </summary>
        /// <param name="reporter">The person who will catch the exception</param>
        /// <param name="work">The work todo</param>
        /// <param name="onException">A handler that will be called when an exception occours, after it is logged by the reporter</param>
        public static T ReportExceptions<T>(this IReportStatus reporter, Func<T> work, Action<Exception> onException = null)
        {
            try
            {
                return work.Invoke();
            }
            catch (Exception e)
            {
                reporter.OnError(e);

                if (onException != null)
                    onException.Invoke(e);
                return default(T);
            }
        }

        /// <summary>
        /// Same are trycatch
        /// Reports any exceptions that occour through your reporter
        /// </summary>
        /// <param name="reporter">The person who will catch the exception</param>
        /// <param name="work">The work todo</param>
        /// <param name="onException">A handler that will be called when an exception occours, after it is logged by the reporter</param>
        public static dynamic ReportExceptions(this IReportStatus reporter, Func<dynamic> work, Func<Exception, dynamic> onException = null)
        {
            try
            {
                return work.Invoke();
            }
            catch (Exception e)
            {
                reporter.OnError(e);

                if (onException != null)
                    return onException.Invoke(e);

                return null;
            }
        }

        /// <summary>
        /// Same are trycatch
        /// Reports any exceptions that occour through your reporter
        /// </summary>
        /// <param name="reporter">The person who will catch the exception</param>
        /// <param name="work">The work todo</param>
        /// <param name="onException">A handler that will be called when an exception occours, after it is logged by the reporter</param>
        public static T ReportExceptions<T>(this IReportStatus reporter, Func<T> work, Func<Exception, T> onException = null)
        {
            try
            {
                return work.Invoke();
            }
            catch (Exception e)
            {
                reporter.OnError(e);

                if (onException != null)
                    return onException.Invoke(e);

                return default(T);
            }
        }


        /// <summary>
        /// Subscribes to a Channel and redirect any error messages received to the IReportStatus interface
        /// to stop the Channel from terminating when something abnormal occours.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="observable">The sequence to observe</param>
        /// <param name="reporter">The reporter to handle the error messages</param>
        /// <param name="onNext">The function that occours when a new message arrives</param>
        /// <param name="onError">The function to execute when an error occours, in addition to the automatic OnError log message produced</param>
        /// <returns>A safely configured observable listener</returns>
        public static IDisposable Subscribe<T>(this IObservable<T> observable, IReportStatus reporter, Action<T> onNext, Action<T, Exception> onError = null, Action<T> onDisposing = null)
        {
            IDisposable sub = null;
            T obj = default(T);
            
            sub = observable.Subscribe(msg =>
            {
                obj = msg;
                try
                {
                    onNext(msg);
                }
                catch (Exception e)
                {
                    reporter.OnError(e);    
                }
            },
            error =>
            {
                reporter.OnError(error);

                if (onError != null)
                    onError(obj, error);
            },
            onCompleted: () =>
            {
                try
                {
                    if (sub != null)
                    {
                        sub.Dispose();
                    }

                    if (onDisposing != null)
                        onDisposing.Invoke(obj);
                }
                catch (Exception e)
                {
                    reporter.OnError(e);
                }
            });

            return sub;
        }


        /// <summary>
        /// Same as subscribe. I think every call should be safe!
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="observable"></param>
        /// <param name="reporter"></param>
        /// <param name="onNext"></param>
        /// <param name="onError"></param>
        /// <param name="onDisposing"></param>
        /// <returns></returns>
        [Obsolete]
        public static IObservable<T> SubscribeSafe<T>(this IObservable<T> observable, IReportStatus reporter, Action<T> onNext, Action<T, Exception> onError = null, Action<T> onDisposing = null)
        {
            IDisposable sub = null;
            T obj = default(T);

            return Observable.Create<T>(o =>
            {
                return observable.Merge(o as IObservable<T>).Subscribe(msg =>
                {
                    obj = msg;
                    try
                    {
                        onNext(msg);
                    }
                    catch (Exception e)
                    {
                        o.OnError(e);
                    }
                },
                error =>
                {
                    reporter.OnError(error);
                    o.OnCompleted();

                    if (onError != null)
                        onError(obj, error);
                },
                onCompleted: () =>
                {
                    try
                    {
                        o.OnCompleted();

                        if (sub != null)
                        {
                            sub.Dispose();

                            if (obj != null && onDisposing != null)
                                onDisposing.Invoke(obj);
                        }
                    }
                    catch (Exception e)
                    {
                        reporter.OnError(e);
                    }
                });
            });
        }
        
        public static IDisposable ReportsOn(this ReportsStatus context, IReportStatus another)
        {
            var resources = new CompositeDisposable();

            another.Errors.Subscribe(context.ReportExceptions).DisposedBy(resources);
            another.Information.Subscribe(context.ReportInformation).DisposedBy(resources);

            return resources;
        }

        public static SystemLogMeta FromMessage(this LogMessage<string> msg)
        {
            return new SystemLogMeta()
            {
                Level = msg.Level.ToString(),
                Reporter = msg.Reporter,
                Message = msg.Message,
                Timestamp = msg.Timestamp
            };
        }

        public static SystemLogMeta FromMessage(this LogMessage<Exception> msg)
        {
            return new SystemLogMeta()
            {
                Level = msg.Level.ToString(),
                Reporter = msg.Reporter,
                Message = msg.Message.Message,
                Timestamp = msg.Timestamp
            };
        }
    }
}
