using System;
using System.Collections.Generic;
using Rxns.Interfaces;

namespace Rxns
{
    /// <summary>
    /// A noop shim to be used when you dont care about rxn history in your system. 
    /// </summary>
    public class NeverAnyEventHistoryProvider : IRxnHistoryProvider
    {
        public IEnumerable<IRxn> GetAll(DateTime? fromDate = null, bool includeReactiveEvents = false)
        {
            return new IRxn[] { };

        }
    }
}
