using System;
using Rxns.Interfaces;

namespace Rxns.WebApiNET5.NET5WebApiAdapters
{
    public interface IEventHubServiceClient : IHubProxyClient
    {
        void RemoteCommand(IRxn cmd);
        IObservable<string> LogMessages { get; }
    }
}
