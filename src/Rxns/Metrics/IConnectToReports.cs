using System;
using System.Collections.Generic;

namespace Rxns.Metrics
{
    public interface IReportUserApi
    {
        void OnUpdate(TimeSeriesData metric); //timeseries data serilised as a delimited seperated list
    }

    public interface IConnectToReports
    {
        IEnumerable<string> LookupReports();
        void ConnectUserToReport(ReportUser user, string reportName);
    }


    public class ReportConnectionContext
    {
        public IDisposable Connection { get; set; }
        public ReportUser User { get; set; }
    }

    public class ReportUser
    {
        private readonly Action<string> _sendFunc;
        public string Id { get; private set; }

        public void Send(string data)
        {
            _sendFunc(data);
        }

        public ReportUser(string id, Action<string> sendFunc)
        {
            Id = id;
            _sendFunc = sendFunc;
        }
    }
}
