using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive.Threading.Tasks;
using System.Security.Principal;
using System.Text;
using Rxns.Interfaces;
using Rxns.Logging;
using Rxns.Playback;

namespace Rxns.Hosting.Cluster
{
    public class NamedPipeClientConnection
    {
        public NamedPipeServerStream Server { get; set; }
        public string ClientId { get; set; }
        public Type[] RespondsTo { get; set; }
        public string ClientName { get; set; }

        public IDisposable RouterCfg { get; set; }
    }

    public class NamedPipesServerBackingChannel : LocalBackingChannel<IRxn>//, IRxnBackingChannel<IRxn>
    {
        private readonly int _macConcurrentClients;
        private readonly Subject<bool> _isOnline = new Subject<bool>();
        public IObservable<bool> IsOnline => _isOnline;
        public string ClientProcessArguments { get; }
        public Dictionary<string, NamedPipeClientConnection> _clients = new Dictionary<string, NamedPipeClientConnection>();

        public static string PipeName = "F7B7AA01-2944-488F-8C23-7F356DD9BEA0";
        public RoutableBackingChannel<IRxn> Router { get; private set; }

        public NamedPipesServerBackingChannel(int macConcurrentClients = 1)
        {
            Router = new RoutableBackingChannel<IRxn>();
            Router.ConfigureWith("local", RxnRouteCfg.OnReactionTo(typeof(IRxn)).PublishTo<IRxn>(m => Router.Local.Publish(m)));
            _macConcurrentClients = macConcurrentClients;
            ClientProcessArguments = $"reactor";
        }

        public void ListenForNewClient(string clientNameAboutToConnect, Type[] routes)
        {
            $"Expecting {clientNameAboutToConnect} to connect with routes {routes.Select(r => r.Name).ToStringEach()}".LogDebug();
            //had an async issue with reactors being created before the routes were added...
            //should probably publish an event instead? or i need to access the routes from here somehow, reffing the container!
            if (!_clients.ContainsKey(clientNameAboutToConnect))
                _clients.Add(clientNameAboutToConnect, new NamedPipeClientConnection() { RespondsTo = routes });
        }
        public override IObservable<IRxn> Setup(IDeliveryScheme<IRxn> postman)
        {
            var resources = new CompositeDisposable();

            return Rxn.Create<IRxn>(o =>
            {
                var waitForCOnnection = Rxn.DfrCreate(() => Rxn.Create(() =>
                 {
                    //something is breaking the pipe, blocking it?
                    //might not be starting/listening on an external thread, and it blocks?
                    //messages are being garbled
                    var pipeServer = new NamedPipeServerStream(PipeName, PipeDirection.InOut, _macConcurrentClients, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                     "WAiting for clients to connect to named pipe server".LogDebug();
                    // Wait for a client to connect

                    var clientName = string.Empty;
                     return pipeServer.WaitForConnectionAsync()
                         .ToObservable(NewThreadScheduler.Default)
                         .SelectMany(_ =>
                         {
                             clientName = HandShake(pipeServer);

                             if (!_clients.ContainsKey(clientName))
                             {
                                 $"unexpected client connection {clientName}".LogDebug();
                                 pipeServer.Disconnect();
                                 return Rxn.Empty();
                             }

                             var connection = _clients[clientName];
                             
                             connection.Server = pipeServer;
                             connection.ClientName = clientName;
                             connection.ClientId = Guid.NewGuid().ToString();
                             connection.RouterCfg = 

                             Router.ConfigureWith(connection.ClientName, RxnRouteCfg.OnReactionTo(connection.RespondsTo).PublishTo<IRxn>(message =>
                             {
                                 if (!connection.Server.IsConnected)
                                 {
                                     "PIPE CLOSED".LogDebug(clientName);
                                     return;
                                 }

                                 try
                                 {
                                     $"^ {connection.ClientName}:{connection.ClientId}: {message.GetType().Name}".LogDebug();
                                     RxnsStream.WriteStream(connection.Server, new UseDeserialiseCodec(), new CapturedRxn(TimeSpan.Zero, message));
                                 }
                                 catch (Exception e)
                                 {
                                     $"{connection.ClientName} => PIPE FAILED => {e}".LogDebug();
                                 }
                             }))
                            ;

                             $"Client '{clientName}' connected to to pipeserver".LogDebug();
                            //need to trampolise here otherwise pipeserver isnt happy
                            _isOnline.OnNext(true);

                             return RxnsStream
                                 .ReadStream<CapturedRxn>(pipeServer, new UseDeserialiseCodec(), true)
                                 .Do(c => o.OnNext(c.Recorded))
                                 .LastOrDefaultAsync()
                                 .Catch<CapturedRxn, Exception>(__ =>
                                 {
                                     $"Pipe connection failed {__}".LogDebug();
                                     return Rxn.Empty<CapturedRxn>();
                                 })
                                 .FinallyR(() =>
                                 {
                                     "Pipeserver Connection ended, expcting restart".LogDebug(clientName);
                                     //"Closing pipeserver".LogDebug();
                                     //pipeServer.Close();
                                     //_clients.Remove(name);

                                     //o.OnCompleted();
                                 });
                         })
                         .Until(GeneralLogging.Log.OnError);
                 })
                .SelectMany(_ => _isOnline)
                .FirstAsync());

                return waitForCOnnection
                    .DoWhile(() => _clients.Count != _macConcurrentClients)
                    .Repeat()
                    .Until(GeneralLogging.Log.OnError);

            })
            .Merge(Router.Setup(postman))
            .Publish()
            .RefCount()
            .FinallyR(() =>
            {
                resources.DisposeAll();
                resources.Clear();

                _clients.Values.Select(v => v.Server).DisposeAll();
                _clients.Clear();
            })
            ;
        }

        /// <summary>
        /// The header can be no longer then 1MB
        /// </summary>
        /// <param name="pipeServer"></param>
        /// <returns></returns>
        private string HandShake(NamedPipeServerStream pipeServer)
        {
            var nameb = new byte[1024];
            var headerBlock = pipeServer.Read(nameb, 0, nameb.Length);
            var name  = Encoding.UTF8.GetString(nameb);

            return name.Substring(0, headerBlock);
        }

        public override void Publish(IRxn message)
        {
            if (_clients.Count < 1)
            {
                $"CANT SEND YET {message.GetType().Name}".LogDebug();
                return;
                //pipeServer.WaitForConnection();
            }

            var didCrash = message as ReactorCrashed;

            if (didCrash != null)
            {
                InValidateClientConnection(didCrash.Name);
                return;
            }


            var newProcess = message as SendReactorOutOfProcess;

            if (newProcess != null)
            {
                _clients.Add(newProcess.Name, new NamedPipeClientConnection()
                {
                    RespondsTo = newProcess.Routes
                });
            }

            Router.Publish(message);
        }

        private void InValidateClientConnection(string name)
        {
            if (!_clients.ContainsKey(name))
            {
                $"Got a crash for a reactor we are not monitoring {name}".LogDebug();
                return;
            }

            var client = _clients[name];
            _clients.Remove(name);

            client.RouterCfg?.Dispose();
            client.Server?.Dispose();

            ListenForNewClient(name, client.RespondsTo);


            "Invalidated existing client connection".LogDebug();
            return;
        }
    }

    public class NamedPipesClientBackingChannel : LocalBackingChannel<IRxn>
    {
        private readonly string _pipeName;
        private readonly string _clientName;
        private Subject<bool> _isOnline = new Subject<bool>();
        private StreamWriter writer;
        private NamedPipeClientStream pipeClient;

        public IObservable<bool> IsOnline => _isOnline;

        public NamedPipesClientBackingChannel(string pipeName, string clientName)
        {
            _pipeName = pipeName;
            _clientName = clientName;
        }

        public override IObservable<IRxn> Setup(IDeliveryScheme<IRxn> postman)
        {
            return Rxn.Create<IRxn>(o =>
                {
                    pipeClient = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Delegation, HandleInheritability.Inheritable);
                    "Connecting to pipe server".LogDebug();
                    // Wait for a client to connect

                    return pipeClient.ConnectAsync()
                        .ToObservable(NewThreadScheduler.Default)
                        .SelectMany(_ =>
                        {
                            "Connected to pipeserver".LogDebug();
                            pipeClient.ReadMode = PipeTransmissionMode.Byte;
                            //need to trampolise here otherwise pipeserver isnt happy

                            Handshake(pipeClient, _clientName);

                            "handshake complete".LogDebug();
                            _isOnline.OnNext(true);

                            return RxnsStream
                                .ReadStream<CapturedRxn>(pipeClient, new UseDeserialiseCodec(), true)
                                .Do(c => o.OnNext(c.Recorded))
                                .LastOrDefaultAsync();
                        })
                        .FinallyR(() =>
                        {
                            "Closing pipeclient".LogDebug();
                            pipeClient.Close();
                            o.OnCompleted();
                        }).Subscribe();
                })
                .Publish()
                .RefCount()
                .Merge(base.Setup(postman));
        }


        public void Handshake(NamedPipeClientStream pipeClient, string clientName)
        {
            var nameb = Encoding.UTF8.GetBytes(clientName);
            pipeClient.Write(nameb, 0, nameb.Length);
            pipeClient.Flush();
            pipeClient.WaitForPipeDrain();
        }
        public override void Publish(IRxn message)
        {
            if (pipeClient == null || !pipeClient.IsConnected)
            {
                $"pipe not connected: {message.GetType().Name}".LogDebug();
                BackingChannel.OnNext(message);
                return;
            }

            try
            {
                if (message.GetType() == typeof(SystemStatusEvent))
                {
                    BackingChannel.OnNext(message);
                }

                RxnsStream.WriteStream(pipeClient, new UseDeserialiseCodec(), new CapturedRxn(TimeSpan.Zero, message));
            }
            catch (Exception e)
            {
                $"PIPE FAILED{_clientName} {e}".LogDebug();
            }
        }
    }
}
