using System;
using System.Linq;
using Rxns.Interfaces;

namespace Rxns
{
    /// <summary>
    /// A fluent route builder that allows you to map rxn classes to specific
    /// rxn managers
    /// </summary>
    public class RxnRouteCfg
    {
        /// <summary>
        /// Builds a route with a function selector based on the incoming rxn
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="when"></param>
        /// <returns></returns>
        public static IRxnTargetCfg OnReactionTo<T>(Func<T, bool> when = null) where T : class
        {
            when = when ?? (_ => true);
            return new EventPublisherBuilder(o => typeof(T).IsAssignableFrom(o.GetType()) && (o is T && when((T)o)));
        }

        /// <summary>
        /// Builds a route for a specific rxn heirachy. All events "assignableFrom"  from the rxn will
        /// be selected by this route
        /// </summary>
        /// <param name="typeOfEvent"></param>
        /// <param name="when"></param>
        /// <returns></returns>
        public static IRxnTargetCfg OnReactionTo(Type typeOfEvent, Func<object, bool> when = null)
        {
            when = when ?? (_ => true);
            return new EventPublisherBuilder(o => typeOfEvent.IsAssignableFrom(o.GetType()) && when(o));
        }

        public static IRxnTargetCfg OnReactionTo(Type[] typeOfEvent, Func<object, bool> when = null)
        {
            when = when ?? (_ => true);
            return new EventPublisherBuilder(o => typeOfEvent.Any(t => t.IsAssignableFrom(o.GetType()) && when(o)));
        }

        /// <summary>
        /// Routes all the events in a stream to this handler. God for catch-all operations
        /// </summary>
        /// <returns></returns>
        public static IRxnTargetCfg OnReaction()
        {
            Func<object, bool> when = _ => true;

            return new EventPublisherBuilder(when);
        }
    }
}
