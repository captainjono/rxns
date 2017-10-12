using System;
using System.Collections.Generic;

namespace Rxns.Interfaces
{
    /// <summary>
    /// Defines a service which known how to lookup the rxn history of a rxn sourced system
    /// </summary>
    public interface IRxnHistoryProvider
    {
        /// <summary>
        /// Returns all the events a system has logged
        /// </summary>
        /// <param name="fromDate">The eariest rxn to return</param>
        /// <param name="includeReactiveEvents">Events that are in reaction to other events. If the aim is to play events back into the system, these events can cause double-actions to occour</param>
        /// <returns></returns>
        IEnumerable<IRxn> GetAll(DateTime? fromDate = null, bool includeReactiveEvents = false);
    }
}
