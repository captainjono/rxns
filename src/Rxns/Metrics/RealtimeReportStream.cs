using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using Rxns.Health.AppStatus;
using Rxns.Logging;

namespace Rxns.Metrics
{
    public class RealtimeReportStream : ReportsStatus
    {
        private readonly ITimeSeriesView _report;
        private readonly IReportConnectionManager _reportMgr;
        private bool _callOnce = true;

        public RealtimeReportStream(ITimeSeriesView report, IReportConnectionManager reportMgr)
        {
            _report = report;
            _reportMgr = reportMgr;

            reportMgr.Connected.Do(ConnectToReport).Until(OnError).DisposedBy(this);
            reportMgr.Disconnected.Do(DisconnectFromReport).Until(OnError).DisposedBy(this);
        }

        private void SendInitalMetricsTo(ReportUser user)
        {
            _report.GetHistory().Do(s => user.Send(s.Value)).Subscribe();
        }

        public void ConnectToReport(ReportUser user)
        {
            this.ReportExceptions(() =>
            {
                OnVerbose("{0} connected", user.Id);
                //SendInitalMetrics(Clients.Caller);

                if (_callOnce)
                {
                    _callOnce = false;
                    MonitorReportForUpdates();
                }
                Observable.Timer(TimeSpan.FromSeconds(1)).Subscribe(this, _ => SendInitalMetricsTo(user));
            });
        }

        public void DisconnectFromReport(ReportUser user)
        {
            this.ReportExceptions(() =>
            {
                OnVerbose("{0} disconnected", user.Id);
            });
        }
        
        private void MonitorReportForUpdates()
        {
            _report.GetUpdates()
                    .Buffer(TimeSpan.FromMilliseconds(100), 20)
                    .Where(ms => ms.Count > 0)
                    .Subscribe(this, metric =>
                    {
                        _reportMgr.OnlineUsers.ForEach(user => user.Send(metric.Serialise()));
                    })
                    .DisposedBy(this);
        }

    }
}
