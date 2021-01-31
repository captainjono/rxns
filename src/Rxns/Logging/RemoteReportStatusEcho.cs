using System;
using Rxns.Interfaces;

namespace Rxns.Logging
{
    public class RemoteReportStatusEcho : IRxnProcessor<RLM>
    {
        public IObservable<IRxn> Process(RLM log)
        {
            ReportStatus.Log.OnMessage(LogLevel.None, log.ToString(), null, log.S);
            return Rxn.Empty();
        }
    }
}
