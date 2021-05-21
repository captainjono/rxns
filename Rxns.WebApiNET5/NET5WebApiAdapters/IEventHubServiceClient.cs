using System;
using Rxns.Interfaces;
using Rxns.WebApiNET5.NET5WebApiAdapters;

namespace RedRain.RedView.Adapter.SignalR
{
    public interface IEventHubServiceClient : IHubProxyClient
    {
        void RemoteCommand(IRxn cmd);
        IObservable<string> LogMessages { get; }
    }
}
