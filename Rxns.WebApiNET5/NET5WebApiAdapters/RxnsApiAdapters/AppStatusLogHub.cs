using System;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Rxns.Health.AppStatus;
using Rxns.Metrics;

namespace Rxns.WebApiNET5.NET5WebApiAdapters.RxnsApiAdapters
{
    /// <summary>
    /// Client-side surface for the AppStatus log hub. Server pushes log entries + periodic
    /// stats refreshes; the client opts in to a system's stream via Subscribe(systemName).
    /// </summary>
    public interface IAppStatusLogClient
    {
        Task LogEntry(AppStatusLogEntry entry);
        Task Stats(AppStatusLogStats stats);
    }

    /// <summary>
    /// Dedicated SignalR hub for the rxns-support portal log dashboard.
    ///
    /// Wire model:
    /// - Connect, then call Subscribe(systemName) one or more times to join a SignalR group.
    /// - The hub's broadcaster subscribes (once) to LocalAppStatusManager's
    ///   Information + Errors streams and forwards each entry to the matching group only.
    /// - Entries with null SystemName go to the "_unscoped_" group so future "all systems"
    ///   pages can opt in without code change.
    ///
    /// This hub is intentionally separate from EventsHub — portal subscribers should not be
    /// flooded by every IRxn the cluster ships, only AppStatusLogEntry payloads.
    /// </summary>
    public class AppStatusLogHub : ReportsStatusEventsHub<IAppStatusLogClient>
    {
        public const string UnscopedGroup = "_unscoped_";

        private static readonly object _broadcasterGate = new object();
        private static bool _broadcasterStarted;

        public AppStatusLogHub(
            IHubContext<AppStatusLogHub, IAppStatusLogClient> context,
            LocalAppStatusManager mgr)
        {
            EnsureBroadcaster(context, mgr);
        }

        public Task Subscribe(string systemName)
        {
            var group = NormaliseGroup(systemName);
            return Groups.AddToGroupAsync(Context.ConnectionId, group);
        }

        public Task Unsubscribe(string systemName)
        {
            var group = NormaliseGroup(systemName);
            return Groups.RemoveFromGroupAsync(Context.ConnectionId, group);
        }

        public override Task OnConnectedAsync()
        {
            this.ReportExceptions(() =>
            {
                OnVerbose("{0} connected", Context.ConnectionId);
            });
            return base.OnConnectedAsync();
        }

        public override Task OnDisconnectedAsync(Exception stopCalled)
        {
            this.ReportExceptions(() =>
            {
                OnVerbose("{0} disconnected", Context.ConnectionId);
            });
            return base.OnDisconnectedAsync(stopCalled);
        }

        private static string NormaliseGroup(string systemName)
        {
            if (string.IsNullOrWhiteSpace(systemName)) return UnscopedGroup;
            return systemName.Trim().ToLowerInvariant();
        }

        private void EnsureBroadcaster(IHubContext<AppStatusLogHub, IAppStatusLogClient> hub, LocalAppStatusManager mgr)
        {
            if (_broadcasterStarted) return;
            lock (_broadcasterGate)
            {
                if (_broadcasterStarted) return;
                try
                {
                    mgr.SubscribeAll(
                        information: info => Push(hub, info.FromMessage()),
                        errors: err => Push(hub, err.FromMessage())
                    );
                    _broadcasterStarted = true;
                }
                catch (Exception ex)
                {
                    OnError(ex);
                }
            }
        }

        private static void Push(IHubContext<AppStatusLogHub, IAppStatusLogClient> hub, SystemLogMeta meta)
        {
            try
            {
                var entry = new AppStatusLogEntry
                {
                    Timestamp = meta.Timestamp,
                    Level = meta.Level,
                    Reporter = meta.Reporter,
                    SystemName = meta.SystemName,
                    Message = meta.Message,
                    StackTrace = meta.StackTrace,
                    ErrorId = meta.ErrorId > 0 ? meta.ErrorId.ToString() : null
                };

                var group = NormaliseGroup(meta.SystemName);
                hub.Clients.Group(group).LogEntry(entry);
            }
            catch
            {
                // never bubble out of the hub's broadcaster
            }
        }
    }
}
