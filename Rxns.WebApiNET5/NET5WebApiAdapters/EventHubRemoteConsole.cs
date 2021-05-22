using System;
using System.Reactive.Linq;
using Rxns.DDD;

namespace Rxns.WebApiNET5.NET5WebApiAdapters
{
    //public class SignalREventHubRemoteConsole : IAppCommandService
    //{
    //    private readonly IEventHubServiceClient _client;
    //    private readonly ISignalRServiceClient _signalrClient;

    //    public SignalREventHubRemoteConsole(IEventHubServiceClient client, ISignalRServiceClient signalrClient)
    //    {
    //        _client = client;
    //        _signalrClient = signalrClient;
    //    }

    //    public IObservable<bool> Connect(string url = null)
    //    {
    //        return _signalrClient.Connect(url).Select(status => status == SyncConnection.Connected);
    //    }

    //    public void SendCommand(string command)
    //    {
    //        _client.RemoteCommand(command);
    //    }

    //    public IObservable<string> LogMessages
    //    {
    //        get { return _client.LogMessages; }
    //    }

    //    public IObservable<object> ExecuteCommand(string route, string command)
    //    {
    //        throw new NotImplementedException();
    //    }
    //}
}
