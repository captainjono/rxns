using System;
using System.Collections.Generic;

namespace Rxns.Interfaces
{
    /// <summary>
    /// Defines a routing table for an specific rxn type
    /// </summary>
    /// <typeparam name="TBaseEvent"></typeparam>
    public interface IRxnRouteCfg<TBaseEvent>
    {
        /// <summary>
        /// If the destinations should be delivered this rxn
        /// </summary>
        IList<Func<object, bool>> Conditions { get; }
        /// <summary>
        /// One or more destinations to the deliver the rxn to if the condition matches
        /// </summary>
        IList<IRxnManager<TBaseEvent>> Destinations { get; }
        /// <summary>
        /// A fluent interface for building a destination list
        /// </summary>
        /// <param name="rxnManager"></param>
        /// <returns></returns>
        IRxnRouteCfg<TBaseEvent> AndTo(IRxnManager<TBaseEvent> rxnManager);
    }
}
