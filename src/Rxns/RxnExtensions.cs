using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Dynamic;
using System.Linq;
using System.Linq.Expressions;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Rxns.Interfaces;
using Rxns.Logging;
using Rxns.Metrics;

namespace Rxns
{
    /// <summary>
    /// A set of highly opinionate extensions that i think make reaction pipeline  
    /// definitions more reliable, more descriptive and easier to reacon about
    /// </summary>
    public static class RxnExtensions
    {
        /// <summary>
        /// The scheduler used to feed the OnReaction pipeline events
        /// Defaults to currentThread
        /// </summary>
        public static IScheduler OnReactionScheduler = CurrentThreadScheduler.Instance;

        /// <summary>
        /// The schduler used for UpdateUiWith
        /// </summary>
        public static IScheduler UIScheduler = CurrentThreadScheduler.Instance;

        /// <summary>
        /// The schduler used for Until() operations
        /// </summary>
        public static IScheduler UntilScheduler = OnReactionScheduler;

        /// <summary>
        /// Sets up a new reaction pipeline. Typically this is followed by an action to be taken 
        /// when TStuff occours
        /// 
        /// advanced: its a simple fluent wrapper for Input.ObserveOn
        /// </summary>
        /// <typeparam name="TStuff"></typeparam>
        /// <param name="context">The reaction component</param>
        /// <param name="scheduler">The scheduler tha the pipeline will use for work. Defaults to OnReactionScheduler</param>
        /// <returns></returns>
        public static IObservable<TStuff> OnReaction<TStuff>(this IReactTo<TStuff> context, IScheduler scheduler = null)
        {
            return context.Input.ObserveOn(scheduler ?? OnReactionScheduler);
        }

        /// <summary>
        /// Sets up a new reaction pipeline. Typically this is followed by an action to be taken 
        /// when TStuff occours
        /// 
        /// advanced: its a simple fluent wrapper for Input.ObserveOn
        /// </summary>
        /// <typeparam name="TStuff"></typeparam>
        /// <param name="context">The reaction component</param>
        /// <param name="scheduler">The scheduler tha the pipeline will use for work. Defaults to OnReactionScheduler</param>
        public static IObservable<TStuff> OnReactionTo<TStuff>(this IReactTo<IRxn> context,
            IScheduler scheduler = null)
        {
            return context.Input.ObserveOn(scheduler ?? OnReactionScheduler).OfType<TStuff>();
        }

        /// <summary>
        /// Sets up a new reaction pipeline. Typically this is followed by an action to be taken 
        /// when TStuff occours
        /// 
        /// advanced: its a simple fluent wrapper for Input.ObserveOn
        /// </summary>
        /// <typeparam name="TStuff"></typeparam>
        /// <param name="stream">An observable you want to use as a quasi reaction component</param>
        /// <param name="scheduler">The scheduler tha the pipeline will use for work. Defaults to OnReactionScheduler</param>
        public static IObservable<TStuff> OnReaction<TStuff>(this IObservable<TStuff> stream,
            IScheduler scheduler = null)
        {
            return stream.ObserveOn(scheduler ?? OnReactionScheduler);
        }

        /// <summary>
        /// A fluent wrapper for Subscibe, when used in the context of a "reaction"
        /// </summary>
        /// <typeparam name="TStuff"></typeparam>
        /// <param name="context"></param>
        /// <param name="errorHandler">The error handler to be invoked if the pipeline fails. Once this fails, you need to setup your reaction again! Defaults to a debug.writeline</param>
        /// <param name="scheduler">The sceduler used for the error handler. defaults to UntilScheduler</param>
        /// <returns></returns>
        public static IDisposable Until<TStuff>(this IObservable<TStuff> context, Action<Exception> errorHandler = null,
            IScheduler scheduler = null)
        {
            errorHandler = errorHandler ?? (e => { "Until() swallowed {0}".FormatWith(e).LogDebug(); });
            IObservable<TStuff> c = context;
            if (scheduler != null) c = c.ObserveOn(scheduler);

            return c.Catch<TStuff, Exception>(e =>
            {
                errorHandler(e);
                return c;
            }).Subscribe(_ => { }, onError: errorHandler);
        }

        /// <summary>
        /// A fluent wrapper for when you want to perform work on the UI Dispatcher / UI EventLoop etc
        /// </summary>
        /// <typeparam name="TStuff"></typeparam>
        /// <param name="context"></param>
        /// <param name="update">The UI manipulation work</param>
        /// <param name="scheduler">Defaults to the UIScheduler if not specified</param>
        /// <returns></returns>
        public static IObservable<TStuff> UpdateUIWith<TStuff>(this IObservable<TStuff> context, Action<TStuff> update,
            IScheduler scheduler = null)
        {
            return context.ObserveOn(UIScheduler).Do(update);
        }

        /// <summary>
        /// Sets up a new reaction pipeline on a component that implements INotifyPropertyChanged. Typically this is followed by an action to be taken 
        /// when TStuff occours
        /// 
        /// advanced: its a simple fluent wrapper for Input.ObserveOn
        /// </summary>
        /// <typeparam name="TStuff"></typeparam>
        /// <param name="stream">An observable you want to use as a quasi reaction component</param>
        /// <param name="scheduler">The scheduler tha the pipeline will use for work. Defaults to OnReactionScheduler</param>
        /// <param name="properties">The properties too watch for changes. The properties can then be referenced by name on the returned dynamic object</param>
        /// <returns>A dynamic object with the properties and their values attached. Reference them by the same name</returns>
        public static IObservable<dynamic> OnReactionTo<T>(this T source, IScheduler scheduler,
            params Expression<Func<T, object>>[] properties) where T : INotifyPropertyChanged
        {
            return Observable.Create<dynamic>(o =>
            {
                try
                {
                    var propertyNames = new Dictionary<string, Func<T, dynamic>>();

                    foreach (var property in properties)
                    {
                        propertyNames.Add(property.GetPropertyInfo().Name, property.Compile());
                    }

                    return IObservableExtensions
                        .FromPropertyChanged<PropertyChangedEventHandler, PropertyChangedEventArgs>(
                            handler => handler.Invoke,
                            h => source.PropertyChanged += h,
                            h => source.PropertyChanged -= h)
                        .ObserveOn(scheduler)
                        .Where(e => propertyNames.ContainsKey(e.EventArgs.PropertyName))
                        .Select(e =>
                        {
                            dynamic d = new ExpandoObject();
                            foreach (var p in propertyNames)
                            {
                                ((IDictionary<string, object>) d)[p.Key] = p.Value.Invoke(source);
                            }

                            return d;
                        })
                        .Subscribe(o);
                }
                catch (Exception e)
                {
                    o.OnError(e);
                    return Disposable.Empty;
                }
            });
        }

        /// <summary>
        /// Sets up a new reaction pipeline on a component that implements INotifyPropertyChanged. This is
        /// 
        /// This method is provided for interop on platforms that dont support the ExpandoObject (dynamic) ie. Xamarin.iOS
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="source"></param>
        /// <param name="scheduler"></param>
        /// <param name="properties">The properties to watch for changes to</param>
        /// <returns>A dictionary with the propertName as the key, and property value which u will need to cast to whatever type they are supposed to be</returns>
        public static IObservable<Dictionary<string, object>> OnReaction<T>(this T source, IScheduler scheduler,
            params Expression<Func<T, object>>[] properties) where T : INotifyPropertyChanged
        {
            return OnReaction(source, false, scheduler, properties);
        }

        /// <summary>
        /// Sets up a new reaction pipeline on a component that implements INotifyPropertyChanged. This is
        /// 
        /// This method is provided for interop on platforms that dont support the ExpandoObject (dynamic) ie. Xamarin.iOS
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="source"></param>
        /// <param name="observeInitial">If the subscription will be alerted as soon as its made, no property change is required</param>
        /// <param name="scheduler"></param>
        /// <param name="properties">The properties to watch for changes to</param>
        /// <returns>A dictionary with the propertName as the key, and property value which u will need to cast to whatever type they are supposed to be</returns>
        public static IObservable<Dictionary<string, object>> OnReaction<T>(this T source, bool observeInitial,
            IScheduler scheduler, params Expression<Func<T, object>>[] properties) where T : INotifyPropertyChanged
        {
            return Rxn.DfrCreate<Dictionary<string, object>>(o =>
                {
                    try
                    {
                        var propertyNames = new Dictionary<string, Func<T, object>>();
                        Func<Dictionary<string, object>> GetValues = () =>
                        {
                            var d = new Dictionary<string, object>();
                            foreach (var p in propertyNames)
                            {
                                d[p.Key] = p.Value.Invoke(source);
                            }

                            return d;
                        };

                        foreach (var property in properties)
                        {
                            propertyNames.Add(property.GetPropertyInfo().Name, property.Compile());
                        }

                        var sub = IObservableExtensions
                            .FromPropertyChanged<PropertyChangedEventHandler, PropertyChangedEventArgs>(
                                handler => handler.Invoke,
                                h => source.PropertyChanged += h,
                                h => source.PropertyChanged -= h)
                            .Where(e => propertyNames.ContainsKey(e.EventArgs.PropertyName))
                            .Select(e => GetValues());

                        if (observeInitial) sub = sub.StartWith(GetValues());

                        return sub.Subscribe(o);
                    }
                    catch (Exception e)
                    {
                        o.OnError(e);
                        return Disposable.Empty;
                    }
                })
                .ObserveOn(scheduler)
                .SubscribeOn(scheduler);
        }

        public static TProperty ValueOf<TKeyType, TProperty>(this IDictionary<string, object> contezxt,
            Expression<Func<TKeyType, TProperty>> propertySelector)
        {
            return (TProperty) contezxt[propertySelector.GetPropertyInfo().Name];
        }

        /// <summary>
        /// Sets up a new reaction pipeline on a component that implements INotifyPropertyChanged. When something changes,
        /// you will receive an anonymous object which has a each property you are listening for on it,
        /// allowing u to safely read its value within you reaction.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="source"></param>
        /// <param name="properties">The properties you want this pipeline to react to</param>
        public static IObservable<dynamic> OnReactionTo<T>(this T source,
            params Expression<Func<T, object>>[] properties) where T : INotifyPropertyChanged
        {
            return source.OnReactionTo(OnReactionScheduler, properties);
        }

        /// <summary>
        /// Sets up a new reaction pipeline on a component that implements INotifyPropertyChanged. This is targeted at a single property. 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="source"></param>
        /// <param name="property">The property you want to watch</param>
        /// <param name="observeInitialValue">The inital/immediate value that this Reaction will see</param>
        public static IObservable<TProperty> OnReaction<T, TProperty>(this T source,
            Expression<Func<T, TProperty>> property, bool observeInitialValue = false, IScheduler scheduler = null)
            where T : INotifyPropertyChanged
        {
            return source.WhenChanged(property, observeInitialValue, scheduler ?? OnReactionScheduler);
        }

        /// <summary>
        /// Allows u to feed one reaction with the output of another reaction
        /// 
        /// A fluent wrapper for "Subscribe"
        /// </summary>
        /// <typeparam name="TStuff"></typeparam>
        /// <param name="context"></param>
        /// <param name="another"></param>
        /// <returns></returns>
        public static IDisposable PipeTo<TStuff>(this IObservable<TStuff> context, IObserver<TStuff> another)
        {
            return context.Subscribe(another);
        }

        /// <summary>
        /// Starts up a task on the specified scheduler
        /// 
        /// A fluent wrapper for obs.Start()
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="scheduler"></param>
        /// <param name="task">the work to run</param>
        /// <returns>The results of the work</returns>
        public static IObservable<T> Run<T>(this IScheduler scheduler, Func<T> task)
        {
            return Observable.Start(() =>
            {
                try
                {
                    return task();
                }
                catch (Exception e)
                {
                    Debug.WriteLine("Uncaught exception while {0}.Run()ing: {1}".FormatWith(scheduler.GetType(), e));
                    return default(T);
                }
            }, scheduler);
        }

        /// <summary>
        /// Starts up a task on the specified scheduler
        /// 
        /// A fluent wrapper for obs.Start()
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="scheduler"></param>
        /// <param name="task">the work to run</param>
        public static IObservable<Unit> Run(this IScheduler scheduler, Action task)
        {
            return Observable.Start(() =>
            {
                try
                {
                    task();
                }
                catch (Exception e)
                {
                    Debug.WriteLine("Uncaught exception while {0}.Run()ing: {1}".FormatWith(scheduler.GetType(), e));
                }
            }, scheduler);

        }

        public static T For<T>(this IRxnResponse context, RxnSource askedBy)
            where T : IRxnResponse
        {
            if(askedBy != null) context.InReplyTo = askedBy.Id;
            return (T) context;
        }

        public static bool IsFor(this IRxnResponse context, RxnSource askedBy)
        {
            return askedBy != null && context.InReplyTo == askedBy.Id;
        }

        public static Func<object, string> SerialiseImpl = msg => msg.ToString();
        public static Func<Type, string, object> DeserialiseImpl = (type, msg) => msg;

        public static string Serialise(this object str)
        {
            return SerialiseImpl(str);
        }

        public static T Deserialise<T>(this string json)
        {
            return (T) DeserialiseImpl(typeof(T), json);
        }
        public static string Serialise(this IEnumerable<TimeSeriesData> data)
        {
            return data.Aggregate<IRxn, string>(null, (current, @event) => "{0}{1}{2}".FormatWith(current, current.IsNullOrWhitespace() ? "" : "", @event.Serialise()));
        }

        public static string Serialise(this IRxn @event)
        {
            return RxnExtensions.Serialise((object)@event).ResolveAs(@event.GetType());
        }
        public static object Deserialise(this string json, Type type)
        {
            return DeserialiseImpl(type, json);
        }

        private static string[] _jsonTypeLeadingFormat = new[] { "\"T\" : \"", "\"T\":\"" };

        public static Type GetTypeFromJson(this string json, IResolveTypes resolver)
        {
            int startToken = 0;
            foreach (var token in _jsonTypeLeadingFormat)
            {
                startToken = json.IndexOf(token, StringComparison.OrdinalIgnoreCase);
                if (startToken > 0)
                {
                    startToken += token.Length;
                    var type = json.Substring(startToken, json.IndexOf('"', startToken) - startToken);
                    var assemblyAndType = type.Split(',');
                    try
                    {
                        return resolver.Resolve(assemblyAndType[0].TrimEnd('\\', '"')).GetType();
                    }
                    catch (TypeLoadException e)
                    {
                        throw new TypeLoadException("Cannot locate the type '{0}' as specified in the json".FormatWith(assemblyAndType[0]), e);
                    }
                    catch (Exception e)
                    {
                        throw new Exception("Could not parse the type from the specified json. Ensure json is created with AttachTypeToJson() extension method", e);
                    }
                };
            }

            throw new Exception("Could not parse the type from the specified json. Ensure json is created with AttachTypeToJson() extension method or has T property;");
        }

        public static string ResolveAs(this string json, Type deserialisedType)
        {
            if (json.IsNullOrWhitespace()) return json;
            return json.Insert(json.IndexOf('{') + 1, "{0}{1}\",".FormatWith(_jsonTypeLeadingFormat[0], deserialisedType.FullName));
        }

        public static T WaitR<T>(this Task<T> task)
        {
            return task.Result;
        }

        public static void WaitR<T>(this Task task)
        {
            task.Wait();
        }
    }

}
