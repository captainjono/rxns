using System;
using Rxns.Interfaces;

namespace Rxns.Interfaces
{
    public interface IRxnPulseService
    {
        TimeSpan Interval { get; set; }
        IObservable<IRxn> Poll();
    }
}
