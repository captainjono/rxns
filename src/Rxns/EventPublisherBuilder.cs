using System;
using System.Collections.Generic;
using Rxns.Interfaces;

namespace Rxns
{
    /// <summary>
    /// A Fluent configuration object. Use the EVentToRouteCfgBuilder to obtain a reference to this class!
    /// </summary>
    public class EventPublisherBuilder : IRxnTargetCfg
    {
        private readonly IList<Func<object, bool>> _conditions;

        public EventPublisherBuilder(Func<object, bool> condition)
        {
            _conditions = new List<Func<object, bool>>(new[] { condition });
        }

        public IRxnRouteCfg<T> PublishTo<T>(Action<T> rxnManager)
        {
            return new EventRouteToManagerCfg<T>(_conditions, rxnManager);
        }
    }
}
