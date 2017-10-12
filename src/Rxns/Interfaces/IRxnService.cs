using System;
using Rxns.Commanding;

namespace Rxns.Interfaces
{
    public interface IRxnService
    {
        IObservable<CommandResult> Start(string from = null, string options = null);
        IObservable<CommandResult> Stop(string from = null);
        IObservable<CommandResult> Setup();
    }
}
