using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Rxns.Metrics;

namespace Rxns.WebApiNET5.NET5WebApiAdapters.RxnsApiAdapters
{
    /// <summary>
    /// The signalr hub used to feed the metrics web pages
    /// 
    /// todo: need to make this generic so i dont need to keep creating different hubs just to service more time-series-data
    /// could use a param that then uses the container to resolve SystemMetricsAggregatedView or whatever view u want that
    /// implements times-series-data
    /// </summary>
    public class SystemMetricsHub : ReportsStatusEventsHub<IReportUserApi>
    {
        private readonly ITimeSeriesView _systemMetricsAggregatedView;
        private readonly IHubContext<SystemMetricsHub> _context;
        private bool callOnce = true;

        public SystemMetricsHub(ITimeSeriesView systemMetricsAggregatedView, IHubContext<SystemMetricsHub> context)
        {
            _systemMetricsAggregatedView = systemMetricsAggregatedView;
            _context = context;
        }

        private void SendInitalMetricsTo(IClientProxy user)
        {
            _systemMetricsAggregatedView.GetHistory().Where(v => v != null).Buffer(TimeSpan.FromSeconds(2), 50).Where(v => v.AnyItems()).Select(v => new TimeSeriesData() { Name = v[0].Name, Value = v.Aggregate((long)0, (a,b) => (long)( a + b.Value) / v.Count) })
                .Do(s => user.SendAsync("onUpdate", s)).Subscribe();
        }

        public override Task OnConnectedAsync()
        {
            this.ReportExceptions(() =>
            {
                OnVerbose("{0} connected", Context.ConnectionId);
                var connected = _context.Clients.Client(Context.ConnectionId);
                
                SendInitalMetricsTo(connected);

                if (callOnce)
                {
                    callOnce = false;
                    ActiveMetricsUpdates();
                }
                Observable.Timer(TimeSpan.FromSeconds(1)).Subscribe(this, _ => SendInitalMetricsTo(connected));
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

        private void ActiveMetricsUpdates()
        {
            _systemMetricsAggregatedView.GetUpdates()
                                        .Buffer(TimeSpan.FromMilliseconds(100), 20)
                                        .Where(ms => ms.Count > 0)
                                        .SelectMany(s => s) //i think i need to reimplement the batched event passing using a delimiter - couldnt find the code in the frontend to deserilise
                                        .Subscribe(this, metric =>
                                        {
                                            _context.Clients.All.SendAsync("onUpdate", metric);
                                        })
                                        .DisposedBy(this);
        }
    }
}
