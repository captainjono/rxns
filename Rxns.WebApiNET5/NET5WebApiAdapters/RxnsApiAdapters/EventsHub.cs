﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Security.Principal;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Rxns.Collections;
using Rxns.DDD;
using Rxns.DDD.Commanding;
using Rxns.Health.AppStatus;
using Rxns.Hosting;
using Rxns.Hosting.Updates;
using Rxns.Interfaces;
using Rxns.Logging;
using Rxns.Microservices;

namespace Rxns.WebApiNET5.NET5WebApiAdapters.RxnsApiAdapters
{
    public interface IHubClientContext
    {
        string ConntectionId { get; }
        IPrincipal User { get; }
    }

    public class SignalRHubClientContext : IHubClientContext
    {
        private readonly HubCallerContext _context;

        public SignalRHubClientContext(HubCallerContext context)
        {
            _context = context;
        }

        public string ConntectionId { get { return _context.ConnectionId; } }
        public IPrincipal User { get { return _context.User; } }
    }

    public interface IAppEventManagerBridge
    {
        void Publish(IRxn cmd);
        void EventReceived(IRxn cmd);
        void EventReceived(RemoteEventReceived cmd);
        void RegisterAsService(string route);
    }

    public interface IAppStatusHub : IAppEventManagerBridge
    {
        void StatusUpdatesSubscribe(IEnumerable<object> statuses);
        void RemoteCommand(RxnQuestion cmd);
        void StatusInitialSubscribe(IEnumerable<object> statuses);
    }

    public class RemoteEventReceived : IRxn
    {
        public string Message { get; set; }
        public string Tenant { get; set; }
        public string Destination { get; set; }
    }

    //[Authorize]
    public class EventsHub : ReportsStatusEventsHub<IAppStatusHub>, IRxnLogger, IAppCmdManager
    {
        private readonly IAppCommandService _cmdService;
        private readonly ISystemStatusStore _statusStore;
        private readonly IHubContext<EventsHub> _context;
        private readonly IAppStatusStore _appStatusStore;
        private readonly IRxnManager<IRxn> _rxnManager;
        private IRxnAppInfo _systeminfo;
        private IDictionary<string, string> _routes = new UseConcurrentReliableOpsWhenCastToIDictionary<string,string>(new ConcurrentDictionary<string, string>());


        public new Action<LogMessage<string>> Information => info =>
        {
            EventReceived(new RemoteEventReceived()
            {
                Message = info.FromMessage().Serialise(),
                Tenant = _systeminfo.Name,
                Destination = "Everyone"
            });
        };

        public new Action<LogMessage<Exception>> Errors => error =>
        {
            EventReceived(new RemoteEventReceived
            {
                Message = error.FromMessage().Serialise(),
                Tenant = _systeminfo.Name,
                Destination = "Everyone"
            });
        };
        
        public EventsHub(IEnumerable<IAppContainer> containers, IAppCommandService cmdService, ISystemStatusStore statusStore, IHubContext<EventsHub> context, IAppStatusStore appStatusStore, IRxnManager<IRxn> rxnManager) //should this be a IRxnPublisher instead? does that work, not sure of lifetimes?
        {
            _cmdService = cmdService;
            _statusStore = statusStore;
            _context = context;
            _appStatusStore = appStatusStore;
            _rxnManager = rxnManager;

            foreach (var container in containers)
            {
                _systeminfo = container.Resolve<IRxnAppInfo>();

                container.SubscribeAll(info =>
                {
                    var si = _systeminfo;
                    EventReceived(new RemoteEventReceived()
                    {
                        Message = info.FromMessage().Serialise(),
                        Tenant = si.Name,
                        Destination = "Everyone"
                    });
                }, error =>
                {
                    var si = _systeminfo;
                    EventReceived(new RemoteEventReceived
                    {
                        Message = error.FromMessage().Serialise(),
                        Tenant = si.Name,
                        Destination = "Everyone"
                    });
                }).DisposedBy((IManageResources)this);
            }

            statusStore.Subscribe(this, s => _context.Clients.All.SendAsync("StatusUpdatesSubscribe", s.Distinct(new TenantOnlyStatusComparer())
                                                                                 .Select(x => new
                                                                                 {
                                                                                     Tenant = x.Key.Tenant,
                                                                                     Systems = s.Keys.Where(k => k.Tenant == x.Key.Tenant)
                                                                                                     .OrderBy(o => o.SystemName)
                                                                                                     .Select(y => new
                                                                                                     {
                                                                                                         System = y,
                                                                                                         Meta = s[y]
                                                                                                     })
                                                                                 }).ToArray()))
                        .DisposedBy(this);
        }

        public override Task OnConnectedAsync()
        {
            this.ReportExceptions(() =>
            {
                if (Context.User == null) return;

                OnVerbose("{0} connected", Context.ConnectionId);
                SendInitalStatus(_context.Clients.Client(Context.ConnectionId));
            });
            return base.OnConnectedAsync();
        }

        private void SendInitalStatus(IClientProxy caller)
        {
            _statusStore.FirstAsync().Subscribe(this,
                s => caller.SendAsync("StatusInitialSubscribe", s.Distinct(new TenantOnlyStatusComparer())
                            .Select(x => new
                            {
                                Tenant = x.Key.Tenant,
                                Systems = s.Keys.Where(k => k.Tenant == x.Key.Tenant)
                                    .OrderBy(o => o.SystemName)
                                    .Select(y => new { System = y, Meta = s[y] })
                            })));
        }

        public override Task OnDisconnectedAsync(Exception exception)
        {
            this.ReportExceptions(() =>
            {
                OnVerbose("{0} disconnected", Context.ConnectionId);

                if (_routes.Count < 1)
                    return;

                var key = _routes.Where(route => route.Value == Context.ConnectionId).Select((item, _) => item.Key);

                if (key.Any())
                    RemoveRegistration(key.First());
            });

            return base.OnDisconnectedAsync(exception);
        }
        
        public IDisposable SendCommand(string route, string command)
        {
            try
            {
                if (Context.User == null)
                {
                    OnWarning("Not logged in. Fix bypass!");
                    //return;
                }

                if (String.IsNullOrWhiteSpace(command))
                {
                    OnWarning("How am I supposed to execute an empty command buddy?");
                    return Disposable.Empty;
                }

                return _cmdService.ExecuteCommand(route, command).Do(result =>
                {
                    OnInformation("{0}", result);
                })
                .Catch<object, Exception>(e =>
                {
                    OnWarning("x {0}", e.Message);
                    return new object().ToObservable();
                }).Until();
            }
            catch (ArgumentException e)
            {
                OnWarning(e.Message);
            }
            catch (Exception e)
            {
                OnError(e);
            }

            return Disposable.Empty;
        }

        public void RegisterAsService(string route)
        {
            var rootRoute = route.AsRootRoute();
            OnVerbose("Registering route for connectionId '{0}' --> '{1}'", Context.ConnectionId, rootRoute);

            _routes.AddOrReplace(rootRoute, Context.ConnectionId);
        }

        public void RemoveRegistration(string route)
        {
            OnVerbose("Removed live route '{0}', commands will now be queued for status signals", route);

            _routes.Remove(route.AsRootRoute());
        }

        public void DisposeSubscription()
        {
            Groups.RemoveFromGroupAsync(Context.ConnectionId, Context.User.Identity.Name);
        }

        public void EventReceived(RemoteEventReceived evt)
        {
            this.ReportExceptions(() =>
            {
                _context.Clients.All.SendAsync("EventReceived", evt);
            });
        }
        public void LogReceived(RemoteEventReceived evt)
        {
            this.ReportExceptions(() =>
            {
                //dont send to services?! only send to listeners?
                _context.Clients.All.SendAsync("LogReceived", evt);
            });
        }

        public IEnumerable<IRxnQuestion> FlushCommands(string route)
        {
            return _appStatusStore.FlushCommands(route);
        }

        public void Publish(IRxn @event)
        {
            _rxnManager.Publish(@event).Until(e => OnError(new Exception($"Failed to publish from remote client! ", e)));
        }

        public void Add(IRxnQuestion cmds)
        {
            var wasFound = false;
            _routes.ForEach(r =>
            {
                if (cmds.IsFor(r.Key))
                {
                    wasFound = true;
                    Clients.Client(r.Value).EventReceived(cmds);
                }
            });

            if (!wasFound)
            {
                _appStatusStore.Add(cmds);
            }
        }
    }
}


