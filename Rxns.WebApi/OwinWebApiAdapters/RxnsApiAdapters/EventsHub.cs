using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using Autofac;
using Microsoft.AspNet.SignalR;
using Microsoft.AspNet.SignalR.Hubs;
using Rxns;
using Rxns.DDD.Commanding;
using Rxns.DDD;
using Rxns.Health.AppStatus;
using Rxns.Hosting;
using Rxns.Interfaces;
using Rxns.Microservices;

namespace Rxns.WebApi.AppStatus.Server
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

    public interface IAppStatusHub
    {
        void StatusUpdatesSubscribe(IEnumerable<object> statuses);
        void RemoteCommand(RxnQuestion cmd);
        void EventReceived(IRxn cmd);
        void StatusInitialSubscribe(IEnumerable<object> statuses);
    }

    public class RemoteEventReceived : IRxn
    {
        public string Message { get; set; }
        public string Tenant { get; set; }
        public string Destination { get; set; }
    }

    //[Authorize]
    public class EventsHub : ReportsStatusEventsHub<IAppStatusHub>
    {
        private readonly IAppCommandService _cmdService;
        private readonly ISystemStatusStore _statusStore;

        public EventsHub(IEnumerable<IAppContainer> containers, IAppCommandService cmdService, ISystemStatusStore statusStore)
        {
            _cmdService = cmdService;
            _statusStore = statusStore;

            foreach (var container in containers)
            {
                var systeminfo = container.Resolve<IRxnAppInfo>();

                container.SubscribeAll(info =>
                {
                    var si = systeminfo;
                    EventReceived(new RemoteEventReceived()
                    {
                        Message = info.FromMessage().Serialise(), //was .FromMessage not TOstring
                        Tenant = si.Name,
                        Destination = "Everyone"
                    });
                }, error =>
                {
                    var si = systeminfo;
                    EventReceived(new RemoteEventReceived
                    {
                        Message = error.FromMessage().Serialise(),
                        Tenant = si.Name,
                        Destination = "Everyone"
                    });
                }).DisposedBy((IManageResources)this);
            }

            statusStore.Subscribe(this, s => Clients.All.StatusUpdatesSubscribe(s.Distinct(new TenantOnlyStatusComparer())
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
                                                                                 })))
                        .DisposedBy(this);
        }

        public override Task OnConnected()
        {
            this.ReportExceptions(() =>
            {
                if (Context.User == null) return;

                OnVerbose("{0} connected", Context.ConnectionId);
                SendInitalStatus(Clients.Caller);
            });

            return base.OnConnected();
        }

        private void SendInitalStatus(dynamic caller)
        {
            _statusStore.FirstAsync().Subscribe(this,
                s => caller.StatusInitialSubscribe(s.Distinct(new TenantOnlyStatusComparer())
                            .Select(x => new
                            {
                                Tenant = x.Key.Tenant,
                                Systems = s.Keys.Where(k => k.Tenant == x.Key.Tenant)
                                    .OrderBy(o => o.SystemName)
                                    .Select(y => new { System = y, Meta = s[y] })
                            })));
        }

        public override Task OnDisconnected(bool stopCalled)
        {
            this.ReportExceptions(() =>
            {
                OnVerbose("{0} disconnected", Context.ConnectionId);

                //if (_routes.Count < 1)
                //    return;

                //var key = _routes.Where(route => route.Value == Context.ConnectionId).Select((item, _) => item.Key);

                //if (key.Any())
                //    RemoveRegistration(key.First());
            });

            return base.OnDisconnected(stopCalled);
        }

        public override Task OnReconnected()
        {
            this.ReportExceptions(() =>
            {
                OnVerbose("{0} reconnected", Context.ConnectionId);
            });


            return base.OnReconnected();
        }

        public void SendCommand(string route, string command)
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
                    return;
                }

                _cmdService.ExecuteCommand(route, command).Do(result =>
                {
                    OnInformation("{0}", result);
                })
                .Catch<object, Exception>(e =>
                {
                    OnWarning("x {0}", e.Message);
                    return new object().ToObservable();
                });
            }
            catch (ArgumentException e)
            {
                OnWarning(e.Message);
            }
            catch (Exception e)
            {
                OnError(e);
            }
        }

        //public void RegisterAsService(string route)
        //{
        //    var rootRoute = route.AsRootRoute();
        //    OnVerbose("Registering route for connectionId '{0}' --> '{1}'", Context.ConnectionId, rootRoute);

        //    //_routes.AddOrReplace(rootRoute, Context.ConnectionId);
        //}

        //public void RemoveRegistration(string route)
        //{
        //    OnVerbose("Removed live route '{0}', commands will now be queued for status signals", route);

        //    _routes.Remove(route.AsRootRoute());
        //}

        public void DisposeSubscription()
        {
            Groups.Remove(Context.ConnectionId, Context.User.Identity.Name);
        }

        public void EventReceived(RemoteEventReceived evt)
        {
            this.ReportExceptions(() =>
            {
                Clients.All.EventReceived(evt);
            });
        }
    }
}


