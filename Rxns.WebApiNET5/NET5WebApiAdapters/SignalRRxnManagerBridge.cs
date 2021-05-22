using System;
using System.Collections.Generic;
using System.IO;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Autofac.Features.OwnedInstances;
using Microsoft.AspNet.SignalR.Client;
using Newtonsoft.Json;
using Rxns.Cloud;
using Rxns.Health;
using Rxns.Hosting;
using Rxns.Interfaces;
using Rxns.Logging;
using ConnectionState = Microsoft.AspNet.SignalR.Client.ConnectionState;

namespace Rxns.WebApiNET5.NET5WebApiAdapters
{
    public class SignalRRxnManagerBridge : HttpAppStatusServiceClient, IEventHubClient, IAppStatusServiceClient
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
        private readonly IRxnManager<IRxn> _eventManager;

        private readonly List<IDisposable> _connectionResources = new List<IDisposable>();
        private readonly List<IDisposable> _isConnectedResources = new List<IDisposable>();
        private readonly IAuthenticationService<AccessToken, ITenantCredentials> _authenticationService;
        private IHubProxy _clientProxy;
        

        public SignalRRxnManagerBridge(Func<string, Owned<HubConnection>> hubClientFactory, IRouteProvider systemInfo, IAuthenticationService<AccessToken, ITenantCredentials> authenticationService, IRxnManager<IRxn> eventManager, IHttpConnection client, ICreateEvents eventFactory, ITenantCredentials credentials, IAppServiceRegistry apps, IScheduler scheduler = null) : base(client, eventFactory, systemInfo, credentials, apps)
        {
            
            _eventManager = eventManager;
            DefaultScheduler = scheduler ?? TaskPoolScheduler.Default;
            _hubClientFactory = hubClientFactory;
            _systemInfo = systemInfo;
            _authenticationService = authenticationService;
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
                        _connection = _hubClientFactory(Url).DisposedBy(_connectionResources);
                        //Add logging to the client
                        var client = _connection.Value; //.ReportsWith(this, _connectionResources);

                        Observable.FromEvent<StateChange>(e => client.StateChanged += e, e => client.StateChanged -= e)
                            .Subscribe(state => OnConnectionStateChanged(state, o))
                            .DisposedBy(_connectionResources);

                        //setup proxy then start
                        _clientProxy = client.CreateHubProxy("EventsHub");
                        OnVerbose("Connecting bridge");

                        //setup authentication
                        return client.WithAuthentication(_authenticationService).Subscribe(_ =>
                        {
                            client.Start()
                                .ToObservable()
                                .Subscribe(t =>
                                {
                                    //do nothing, because state-change is more reliable
                                    //way to tell if the connection is ready
                                },
                                error =>
                                {
                                    o.OnError(error);
                                });
                        },
                        error =>
                        {
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

        private void OnConnectionStateChanged(StateChange state, IObserver<Unit> connectionStateStream)
        {
            switch (state.NewState)
            {

                //disconnection is only an error when a client is already connecting/connected 
                case ConnectionState.Reconnecting:
                    OnVerbose("Reconnecting");
                    break;
                case ConnectionState.Disconnected:
                    OnVerbose("Disconnecting");
                    _isConnected.OnNext(false);
                    _isConnectedResources.DisposeAll();
                    _isConnectedResources.Clear();
                    break;
                case ConnectionState.Connected:
                    OnVerbose("Connected");
                    //_route =
                        //RemoteCommandEvent.ForTenant<RemoteCommandEvent>(_configuration.Tenant, _systemInfo.Name, ReporterName).Destination;
                    _clientProxy.Invoke("RegisterAsService", _systemInfo.GetLocalBaseRoute());
                    //setup the publish channel
                    publishChannel
                        //.Buffer(TimeSpan.FromSeconds(2), DefaultScheduler)
                        .Subscribe(this, msg =>
                        {
                            _clientProxy.Invoke("Publish", msg);
                        })
                        .DisposedBy(_isConnectedResources);

                    _clientProxy.On<IRxn>("RemoteCommand", action =>
                    {
                        _eventManager.Publish(action);
                    }).DisposedBy(_isConnectedResources);

                    _clientProxy.On<IRxn>("EventReceived", action =>
                    {
                        _eventManager.Publish(action);
                    }).DisposedBy(_isConnectedResources);


                    if(false) //need to make configurable. this can flood otherwise
                        _clientProxy.On<IRxn>("LogReceived", action =>
                        {
                            _eventManager.Publish(action);
                        }).DisposedBy(_isConnectedResources);


                    _isConnected.OnNext(true);
                    connectionStateStream.OnNext(new Unit());
                    connectionStateStream.OnCompleted();
                    break;
            }
        }

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
                    _clientProxy.Invoke("RemoveRegistration", _systemInfo.GetLocalBaseRoute());
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

                _clientProxy.On<string>("EventReceived",
                    (message) =>
                        this.ReportExceptions(() =>
                        {
                            var msg = JsonConvert.DeserializeObject<IRxn>(message.ToString());
                            _remoteEvents.OnNext(msg);
                        }))
                    .DisposedBy(_connectionResources);
            }

            return _remoteEvents;
        }

        public IObservable<Unit> Publish(IEnumerable<IRxn> events)
        {
            return Rxn.Create(() => Publish(events));
        }

        public IObservable<Unit> PublishError(BasicErrorReport report)
        {
            return Rxn.Create(() => Publish(report));
        }

        public IObservable<Unit> DeleteError(long id)
        {
            throw new NotImplementedException();
        }

        public IObservable<IRxnQuestion[]> PublishSystemStatus(SystemStatusEvent status, AppStatusInfo[] meta)
        {
            "FIXME: appcmds not received? do we care? already received on the other channel yeh? of need to flush that store still? hmm how?".LogDebug();
            return Rxn.Create<IRxnQuestion[]>(() => Publish(new AppHeatbeat(status, meta)));

        }

        public IObservable<string> PublishLog(Stream zippedLog)
        {
            throw new NotImplementedException();
        }
    }
}
