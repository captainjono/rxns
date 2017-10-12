using System;

namespace Rxns.Interfaces
{
    /// <summary>
    /// An rxn processor listens to an rxn queue and performs specific actions
    /// for messages it is interested in
    /// </summary>
    public interface IRxnProcessor<T>
    {   
        IObservable<IRxn> Process(T @event);
    }   
}
