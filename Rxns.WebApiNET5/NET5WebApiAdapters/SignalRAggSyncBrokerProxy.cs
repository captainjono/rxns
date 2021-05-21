using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Rxns.NewtonsoftJson;

namespace Rxns.WebApiNET5.NET5WebApiAdapters
{
    public interface IAggSyncClient
    {
        Task ToSync(string @event);
        Task SyncStatus(string current);
    }

 //   [Authorize]
    public class SignalRAggSyncBrokerProxy : ReportsStatusEventsHub<IAggSyncClient>, IAggSyncBroker
    {
        public class BrokerConnection
        {
            public readonly List<IDisposable> ConnectionResources = new List<IDisposable>();

            //public THread User { get; set; }
            public DateTime LastHeartBeat { get; set; }
            public IAggSyncControls Controls { get; set; }
            public bool IsActive { get; set; }
        }

        private readonly IAggSyncBroker _broker;
        public IDictionary<string, BrokerConnection> Connections { get; private set; }

        public SignalRAggSyncBrokerProxy(IAggSyncBroker broker, IDictionary<string, BrokerConnection> connectionCache)
        {
            _broker = broker;
            Connections = connectionCache;
        }

        public override Task OnConnectedAsync()
        {
            this.ReportExceptions(() =>
            {
                if (Context.User == null) return; //just got this working, i need to properly send a token so we get context here

                OnVerbose("{0} connected", Context.ConnectionId);
                TrackUser(Context);
            });

            return base.OnConnectedAsync();
        }

        public override Task OnDisconnectedAsync(Exception exception)
        {
            this.ReportExceptions(() =>
            {
                if (Context.User == null) return;

                OnVerbose("{0} disconnected", Context.ConnectionId);
                UntrackUser(Context);
            });

            
            return base.OnDisconnectedAsync(exception);
        }


        private void TrackUser(HubCallerContext context)
        {
            if (Connections.ContainsKey(context.ConnectionId)) return;

            Connections.Add(context.ConnectionId, new BrokerConnection()
            {
                //User = new UserContext(context.User),
                LastHeartBeat = DateTime.Now
            });
        }

        private void ReTrackUser(HubCallerContext context)
        {
            TrackUser(context);
            //todo: need to reconnect all the syncing aggs, either locally or remotely
        }

        private void UntrackUser(HubCallerContext c)
        {
            var context = Connections[c.ConnectionId];

            if (context.ConnectionResources != null)
            {
                context.ConnectionResources.DisposeAll();
                context.ConnectionResources.Clear();
            }

            Connections.Remove(c.ConnectionId);
        }

        public void RegisterAsSlave(string clientId)
        {
            TrackUser(Context);
            var remote = Context.ConnectionId;
            var local = Connections[remote];

            RegisterSlave("local.User.Tenant", "local.User.UserName", Context.ConnectionId)
                .Subscribe(this, controls =>
                {
                    local.Controls = controls.DisposedBy(local.ConnectionResources);

                    if (!local.IsActive)
                    {
                        ConnectLocalControlsToRemoteClient(local, remote);
                        local.IsActive = true;
                        new DisposableAction(() => local.IsActive = false).DisposedBy(local.ConnectionResources);
                    }
                })
                .DisposedBy(local.ConnectionResources);
        }

        public void Sync(string[] aggIds, AggregateType type, AggSyncLevel level, string lastEventId = null)
        {
            OnVerbose("sync request received");
            var local = Connections[Context.ConnectionId];

            if (local.Controls == null) throw new Exception("Need to register before trying this operation");
            local.Controls.Sync(aggIds, type, level, lastEventId == null ? (Guid?)null : Guid.Parse(lastEventId));
            //not transfering the lastEventId over for sdome reason!... ther is another comment in another window 2
        }

        private void ConnectLocalControlsToRemoteClient(BrokerConnection context, string connectionId)
        {
            OnInformation("Connecting local to remote for '{0}:{1}'", /*context.User.UserName*/ "fixme", connectionId);
            context.Controls.ToSync()
                .Buffer(TimeSpan.FromMilliseconds(100), 20)
                .Where(e => e.Count > 0)
                .Do(async @events =>
                {
                    await Clients.Client(connectionId).ToSync(@events.ToJson());
                })
                .Until(OnError)
                .DisposedBy(context.ConnectionResources);

            context.Controls.SyncStatus()
                .Do(async current =>
                {
                    await Clients.Client(connectionId).SyncStatus(current.ToJson());
                })
                .Until(OnError)
                .DisposedBy(context.ConnectionResources);
        }

        public void Confirm(Guid[] eventIds)
        {
            var context = Connections[Context.ConnectionId];
            if (context.Controls == null) throw new Exception("Need to register before trying this operation");

            context.Controls.Confirm(eventIds);
        }

        public IObservable<IAggSyncControls> RegisterSlave(string tenant, string userName, string clientId)
        {
            return _broker.RegisterSlave(tenant, userName, clientId);
        }

        public IObservable<IAggPublishControls> RegisterMaster(string clientId, string tenant, string userName)
        {
            return _broker.RegisterMaster(tenant, userName, clientId);
        }
    }
}
