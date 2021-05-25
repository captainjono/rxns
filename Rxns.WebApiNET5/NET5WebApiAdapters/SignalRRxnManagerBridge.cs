using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive.Threading.Tasks;
using System.Threading.Tasks;
using Autofac.Features.OwnedInstances;
using Microsoft.AspNet.SignalR.Client;
using Microsoft.AspNetCore.SignalR.Client;
using Newtonsoft.Json;
using Rxns.Cloud;
using Rxns.Health;
using Rxns.Hosting;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns.WebApiNET5.NET5WebApiAdapters
{
    public class SignalRRxnManagerBridge : HttpAppStatusServiceClient, IEventHubClient, IAppStatusServiceClient, IRxnPublisher<IRxn> //ibackingchannel
    {
        public IScheduler DefaultScheduler { get; set; }

        public string Url { get; set; }

        private readonly BehaviorSubject<bool> _isConnected = new BehaviorSubject<bool>(false);
        public IObservable<bool> IsConnected
        {
            get { return _isConnected; }
        }

        private Subject<IRxn> _remoteEvents;
        private readonly Func<string, Owned<HubConnection>> _hubClientFactory;
        private readonly IRouteProvider _systemInfo;
        private Owned<HubConnection> _connection;

        private readonly Subject<IRxn> publishChannel = new Subject<IRxn>();

        private readonly List<IDisposable> _connectionResources = new List<IDisposable>();
        private readonly List<IDisposable> _isConnectedResources = new List<IDisposable>();
        private readonly IAuthenticationService<AccessToken, ITenantCredentials> _authenticationService;
        //private IHubProxy _clientProxy;
        private Action<IRxn> _publish;
        private IResolveTypes _resolver;


        public SignalRRxnManagerBridge(Func<string, Owned<HubConnection>> hubClientFactory, IRouteProvider systemInfo, IRxnAppInfo appInfo, IAuthenticationService<AccessToken, ITenantCredentials> authenticationService, IHttpConnection client, ICreateEvents eventFactory, ITenantCredentials credentials, IAppServiceRegistry apps, IResolveTypes resolver, IScheduler scheduler = null) : base(client, eventFactory, appInfo, credentials, apps)
        {
            _publish = msg => { "NOT SETUP YET, WONT SEND".LogDebug(msg.GetType()); };
            DefaultScheduler = scheduler ?? TaskPoolScheduler.Default;
            _hubClientFactory = hubClientFactory;
            _systemInfo = systemInfo;
            _authenticationService = authenticationService;
            _resolver = resolver;

            Url = WithBaseUrl("EventsHub");
            Connect().Until(OnError);
        }

        /// <summary>
        /// Connects to the SignalR hub specified in the Url
        /// </summary>
        public IObservable<Unit> Connect()
        {
            return Observable.Create<Unit>(o =>
            {
                try
                {
                    if (_connection == null)
                    {
                        OnInformation("Connecting to: '{0}'", Url);

                        //get new client from factory
                        _connection = _hubClientFactory(Url); //singleton now //.DisposedBy(_connectionResources);
                        //Add logging to the client
                        var client = _connection.Value; //.ReportsWith(this, _connectionResources);

                        Action connect = null;
                        connect = () =>
                        {

                            //already connecting?
                            if(client.State != HubConnectionState.Disconnected) return;

                            lock (_isConnectedResources)
                            {
                                _isConnectedResources.DisposeAll();
                                _isConnectedResources.Clear();
                            }

                            TimeSpan.FromSeconds(1).Then().SelectMany(_ =>
                                    
                            client.StartAsync()
                                .ToObservable()
                                .Do(t =>
                                {
                                    _isConnected.OnNext(true);

                                    lock (_isConnectedResources)
                                    {
                                        client.InvokeAsync("RegisterAsService", _systemInfo.GetLocalBaseRoute());
                                        //setup the publish channel
                                        publishChannel
                                            //.Buffer(TimeSpan.FromSeconds(2), DefaultScheduler) //todo: add buffing back in, use string[] ? delim?
                                            .Subscribe(this,
                                                msg =>
                                                {
                                                    client.InvokeAsync("Publish",
                                                        msg.Serialise().ResolveAs(msg.GetType()));
                                                })
                                            .DisposedBy(_isConnectedResources);

                                        _isConnected.OnNext(true);


                                        client.On<IRxn>("RemoteCommand", action => { _publish(action); })
                                            .DisposedBy(_isConnectedResources);

                                        //should be called in createsubscript, not here
                                        client.On<string>("Subscribe",
                                            action =>
                                            {
                                                _publish((IRxn) action.Deserialise(action.GetTypeFromJson(_resolver)));
                                            }).DisposedBy(_isConnectedResources);
                                    }

                                })
                            )
                            .Until(e =>
                            {
                                o.OnError(e);
                                connect();
                            });
                        };


                        client.Reconnecting += exception =>
                        {
                            OnError("Reconnecting!", exception);
                            _isConnected.OnNext(false);

                            return Task.CompletedTask;
                        };

                        client.Reconnected += s =>
                        {
                            _isConnected.OnNext(true);

                            return Task.CompletedTask;
                        };

                        client.Closed += exception =>
                        {
                            "Connection closed!".LogDebug(Url);
                            _isConnected.OnNext(false);

                            connect();

                            return Task.CompletedTask;
                        };

                        //setup proxy then start
                        OnVerbose("Connecting bridge");

                        //setup authentication
                        return client.WithAuthentication(_authenticationService).Subscribe(_ =>
                        {
                            connect();

                        },
                        error =>
                        {
                            _isConnected.OnNext(false);

                            o.OnError(error);
                        })
                        .DisposedBy(_connectionResources);
                    }
                }
                catch (Exception e)
                {
                    OnError(e);
                }

                return Disposable.Empty;
            });
        }

        //private void OnConnectionStateChanged(ConnectionState state, IObserver<Unit> connectionStateStream)
        //{
        //    switch (state.NewState)
        //    {

        //        //disconnection is only an error when a client is already connecting/connected 
        //        case ConnectionState.Reconnecting:
        //            OnVerbose("Reconnecting");
        //            break;
        //        case ConnectionState.Disconnected:
        //            OnVerbose("Disconnecting");
        //            _isConnected.OnNext(false);
        //            _isConnectedResources.DisposeAll();
        //            _isConnectedResources.Clear();
        //            break;
        //        case ConnectionState.Connected:
        //            OnVerbose("Connected");
        //            //_route =
        //                //RemoteCommandEvent.ForTenant<RemoteCommandEvent>(_configuration.Tenant, _systemInfo.Name, ReporterName).Destination;
                  



        //            if(false) //need to make configurable. this can flood otherwise
        //                _clientProxy.On<IRxn>("LogReceived", action =>
        //                {
        //                    _publish(action);
        //                }).DisposedBy(_isConnectedResources);


        //            _isConnected.OnNext(true);
        //            connectionStateStream.OnNext(new Unit());
        //            connectionStateStream.OnCompleted();
        //            break;
        //    }
        //}

        /// <summary>
        /// Disconnects from the SignalR hub
        /// </summary>
        public void Disconnect()
        {
            if (_connection != null)
            {
                OnInformation("Disconnecting client");

                this.ReportExceptions(() =>
                {
                    _connection.Value.InvokeAsync("RemoveRegistration", _systemInfo.GetLocalBaseRoute());
                });

                _isConnected.OnNext(false);
                _isConnectedResources.DisposeAll();
                _isConnectedResources.Clear();

                //dispose all resources
                _connectionResources.DisposeAll();
                _connectionResources.Clear();
                _connection = null;

            }
        }

        public void Publish(IRxn message)
        {
            this.ReportExceptions(() =>
            {
                publishChannel.OnNext(message);//.AsRemote());
            });
        }


        public IObservable<IRxn> CreateSubscription()
        {
            OnInformation("Creating subscription for remote events --- NEED TO FIX - WONT WORK! - need to deserilise properly here");

            if (_remoteEvents == null)
            {
                _remoteEvents = new Subject<IRxn>();

                _connection.Value.On<string>("Subscribe",
                    (message) =>
                        this.ReportExceptions(() =>
                        {
                            var msg = message.Deserialise(message.GetTypeFromJson(_resolver));
                            _remoteEvents.OnNext((IRxn)msg);
                        }))
                    .DisposedBy(_connectionResources);
            }

            return _remoteEvents;
        }

        public override IObservable<Unit> Publish(IEnumerable<IRxn> events)
        {
            return Rxn.Create(() =>
            {
                events.ForEach(e => Publish(e));
            });
        }

        public override IObservable<Unit> PublishError(BasicErrorReport report)
        {
            return Rxn.Create(() => Publish(report));
        }

        public override IObservable<IRxnQuestion[]> PublishSystemStatus(SystemStatusEvent status, AppStatusInfo[] meta)
        {
            "FIXME: appcmds not received? do we care? already received on the other channel yeh? of need to flush that store still? hmm how?".LogDebug();
            return Rxn.Create<IRxnQuestion[]>(() => Publish(new AppHeartbeat(status, meta)));
        }

        public void ConfigiurePublishFunc(Action<IRxn> publish)
        {
            _publish = publish;
        }
    }
}
