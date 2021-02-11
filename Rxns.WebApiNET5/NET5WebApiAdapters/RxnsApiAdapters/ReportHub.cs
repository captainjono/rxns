using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Rxns.Health.AppStatus;
using Rxns.Logging;
using Rxns.Metrics;

namespace Rxns.WebApiNET5.NET5WebApiAdapters.RxnsApiAdapters
{
    public class SignalRReportConnectionManager : ReportsStatusEventsHub<IReportUserApi>, IReportConnectionManager
    {
        protected readonly Subject<ReportUser> _connected = new Subject<ReportUser>();
        protected readonly Subject<ReportUser> _disconnected = new Subject<ReportUser>();

        public IObservable<ReportUser> Connected => _connected;
        public IObservable<ReportUser> Disconnected => _disconnected;

        protected readonly IDictionary<string, ReportUser> _onlineUsers = new ConcurrentDictionary<string, ReportUser>();
        public IEnumerable<ReportUser> OnlineUsers => _onlineUsers.Values;


        //need to split these out into anothe rlower level class - generic signalr hub
        public override Task OnConnectedAsync()
        {
            this.ReportExceptions(() =>
            {
                var user = new ReportUser(Context.ConnectionId, msg => { }); //Clients.Caller.OnUpdate(msg);
            
                OnVerbose("{0} connected", user.Id);
                _onlineUsers.Add(user.Id, user);
                _connected.OnNext(user);                
            });

            return base.OnConnectedAsync();
        }

        public override Task OnDisconnectedAsync(Exception exception)
        {
            this.ReportExceptions(() =>
            {
                OnVerbose("{0} disconnected", Context.ConnectionId);

                _disconnected.OnNext(new ReportUser(Context.ConnectionId, msg => OnVerbose($"[Not Connected] {msg}")));
            });
            return base.OnDisconnectedAsync(exception);
        }
    }

    public class SystemMetricsReport : SignalRReportConnectionManager
    {
        private readonly IConnectToReports _hub;
        public SystemMetricsReport(IConnectToReports hub)
        {
            _hub = hub;
        }

        public IEnumerable<string> LookupReports()
        {
            return _hub.LookupReports();
        }

        public void ConnectToReport(string reportName)
        {
            _hub.ConnectUserToReport(new ReportUser(Context.ConnectionId, msg => { }), reportName);//Clients.Caller.OnUpdate(msg);

        }
    }

    /// <summary>
    /// Returns the event sync metrics
    /// </summary>
    public class ReportHub : ReportsStatusEventsHub<IConnectToReports>, IConnectToReports
    {
        public readonly IDictionary<string, ReportConnectionContext> CurrentReportUsers = new Dictionary<string, ReportConnectionContext>();
        private readonly ITimeSeriesView[] _reports;
        public ReportHub(ITimeSeriesView[] reports)
        {
            _reports = reports;
        }

        private void TrackUserOfReport(ReportUser user, ReportConnectionContext connection)
        {
            UnTrackUserOfReport(user);

            CurrentReportUsers.Add(user.Id, connection);
        }

        private void UnTrackUserOfReport(ReportUser user)
        {
            if (CurrentReportUsers.ContainsKey(user.Id))
            {
                CurrentReportUsers[user.Id].Connection.Dispose();
                CurrentReportUsers.Remove(user.Id);
            }
        }

        public void ConnectUserToReportDirect(ReportUser user, ITimeSeriesView report)
        {
            var reportStream = report.GetHistory().Concat(report.GetUpdates());
            var context = new ReportConnectionContext()
            {
                Connection = reportStream
                    .Where(e => e != null)
                    .Buffer(TimeSpan.FromMilliseconds(100), 20)
                    .Where(ms => ms.Count > 0)
                    .Subscribe(this, metrics => { user.Send(metrics.Serialise()); }),
                User = user
            };

            TrackUserOfReport(user, context);
        }

        public IEnumerable<string> LookupReports()
        {
            return _reports.Select(r => r.ReportName);
        }

        public void ConnectUserToReport(ReportUser user, string reportName)
        {
            var report = _reports.FirstOrDefault(r => r.ReportName == reportName);
            if (report == null)
            {
                throw new Exception("Report '{0}' not found".FormatWith(reportName));
            }


            ConnectUserToReportDirect(user, report);
        }
    }
}

