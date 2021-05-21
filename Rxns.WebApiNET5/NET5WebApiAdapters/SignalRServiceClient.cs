using System;
using System.Collections.Generic;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Autofac.Features.OwnedInstances;
using Microsoft.AspNet.SignalR.Client;
using Rxns.Hosting;
using Rxns.Logging;

namespace Rxns.WebApiNET5.NET5WebApiAdapters
{
    public interface ISignalRServiceClient
    {
        HubConnection Client { get; }

        IObservable<SyncConnection> Connect(string url = null);
    }

    public interface IHubProxyClient
    {
        void SetupHubProxy(HubConnection client);
    }

    public class SignalRServicesClient : ReportsStatus, ISignalRServiceClient
    {
        private readonly IAuthenticationService<UserAccessToken, IUserCredentials> _authService;
        private string _url;
        private readonly Func<string, Owned<HubConnection>> _urlToHubClient;
        private readonly Func<IHubProxyClient[]> _clientFactory;
        public HubConnection Client { get; private set; }
        
        public SignalRServicesClient(string url, string token, Func<string, Owned<HubConnection>> urlToHubClient, Func<IHubProxyClient[]> clientFactory)
        {
            _urlToHubClient = urlToHubClient;
            _clientFactory = clientFactory;
            Client = urlToHubClient(url).DisposedBy(this).Value;
            Client.Headers.Add("Authorization", "bearer {0}".FormatWith(token));
        }

        public SignalRServicesClient(IRxnAppCfg configuration, IAuthenticationService<UserAccessToken, IUserCredentials> authService, Func<string, Owned<HubConnection>> urlToHubClient, Func<IHubProxyClient[]> clientFactory)
        {
            _authService = authService;
            _urlToHubClient = urlToHubClient;
            _clientFactory = clientFactory;
            _url = $"{configuration.AppStatusUrl}/SignalR";
        }

        public IObservable<SyncConnection> Connect(string url = null)
        {
            var resources = new CompositeDisposable();

            return Rxn.DfrCreate(() =>
            {
                if (url != null) url = url + "/SignalR";
                if (Client == null)
                {
                    Client = _urlToHubClient(url ?? _url).DisposedBy(resources).Value.DisposedBy(resources);
                }

                return _authService.Login(null).Do(token =>
                {
                    OnVerbose("Setting new token");
                    Client.Headers.Add("Authorization", "bearer {0}".FormatWith(token.Token));
                })
                .Select(_ => Rxn.Create<SyncConnection>(o =>
                {
                    OnInformation("Connecting to: '{0}'", url ?? _url);

                    Observable.FromEvent<StateChange>(e => Client.StateChanged += e, e => Client.StateChanged -= e)
                        .Subscribe(state => o.OnNext(Translate(state)))
                        .DisposedBy(resources);

                    Observable.FromEvent<Exception>(e => Client.Error += e, e => Client.Error -= e)
                        .Subscribe(e => OnError(e))
                        .DisposedBy(resources);

                    //setup proxy then start
                    _clientFactory().ForEach(c => c.SetupHubProxy(Client));
                    Func<IDisposable> startConnection = () => Disposable.Empty;
                    startConnection = () => Client.Start()
                        .ToObservable()
                        .Subscribe(
                            __ =>
                            {
                                /*state change handles our connection state */
                            },
                            e =>
                            {
                                o.OnError(e);
                                //already try reconnecting
                                Observable.Timer(TimeSpan.FromSeconds(5)).Subscribe(this, __ => startConnection());
                            })
                        .DisposedBy(resources);

                    return resources;
                }));
            })
            .Switch()
            .ObserveOn(NewThreadScheduler.Default);
        }

        private SyncConnection Translate(StateChange state)
        {
            return (SyncConnection)state.NewState;
        }
    }

}
