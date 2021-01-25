using System;
using System.Collections.Generic;
using Rxns.Interfaces;

namespace Rxns
{
    /// <summary>
    /// A basic impelemntation of a rxn => rxnManager route
    /// </summary>
    /// <typeparam name="TBaseEvent"></typeparam>
    public class EventRouteToManagerCfg<TBaseEvent> : IRxnRouteCfg<TBaseEvent>
    {
        public IList<Func<object, bool>> Conditions { get; private set; }
        public IList<Action<TBaseEvent>> Destinations { get; private set; }

        public EventRouteToManagerCfg(IList<Func<object, bool>> conditions, Action<TBaseEvent> rxnManager)
        {
            Conditions = conditions;
            Destinations = new List<Action<TBaseEvent>>(new[] { rxnManager  });
        }

        public IRxnRouteCfg<TBaseEvent> AndTo(Action<TBaseEvent> rxnManager)
        {
            Destinations.Add(rxnManager);

            return this;
        }
    }
}
