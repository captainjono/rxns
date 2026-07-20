using System;
using System.Reactive.Subjects;
using Rxns.DDD.Commanding;

namespace Rxns.Interfaces
{
    /// <summary>
    /// Defines a reactive query which changes its result as the filter and continution streams progress
    /// It can also synchronise with other queries to a series of queries that react to the same inputs.
    /// </summary>
    /// <typeparam name="T">The result of the query</typeparam>
    /// <typeparam name="TFilter">The type of filter the query operation understands</typeparam>
    public interface IObservableQuery<T, TFilter> : IObservable<T>, IManageResources
    {
        /// <summary>
        /// A stream of filter operations that mutate the query
        /// </summary>
        ISubject<TFilter> Filter { get; set; }
        /// <summary>
        /// The limiter / pager of the query
        /// </summary>
        ISubject<ContinuationToken> Continuation { get; set; }
        /// <summary>
        /// Another query to sync  its Filter / Continuation streams with
        /// </summary>
        /// <param name="another">The seperate but releated query</param>
        /// <returns>A synchronised query</returns>
        IObservableQuery<T, TFilter> SyncWith(IObservableQuery<T, TFilter> another);
        /// <summary>
        /// Any errors that occour while executing the query can be observed here. The idea is 
        /// that your query will be resiliant, and never be broken, but u may still want to log
        /// and react to when something unexpected happens
        /// </summary>
        IObservable<Exception> Errors { get; }
    }
}
