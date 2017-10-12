using System;
using Rxns.Interfaces;

namespace Rxns
{
    public class GarenteedToNeverBeLost<IRxn> : IDeliveryScheme<IRxn>
    {
        public void Deliver(IRxn @event, Action<IRxn> postBox)
        {
            throw new NotImplementedException();
        }

        public IObservable<IRxn> Deliver(IRxn @event, Func<IRxn, IObservable<IRxn>> postBox)
        {
            throw new NotImplementedException();
        }
    }
}
