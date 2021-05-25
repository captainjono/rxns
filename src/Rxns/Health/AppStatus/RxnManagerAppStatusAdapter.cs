using System;
using System.Reactive;
using System.Reactive.Linq;
using Rxns.Cloud;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns.Health.AppStatus
{
    public class RxnManagerSystemStatusAdapter : ReportStatus, IRxnProcessor<AppHeartbeat>, IRxnProcessor<BasicErrorReport>
    {
        private readonly IAppStatusManager _appStatus;
        private readonly IAppErrorManager _appErrors;

        public RxnManagerSystemStatusAdapter(IAppStatusManager appStatus, IAppErrorManager appErrors)
        {
            _appStatus = appStatus;
            _appErrors = appErrors;
        }

        public IObservable<IRxn> Process(AppHeartbeat status)
        {
            return Rxn.Create<IRxn>(() =>
            {
                try
                {

                    if (status?.Status == null)
                    {
                        OnWarning("Unknown status received from {0}: {1}", "unknown", status);
                        return new IRxnQuestion[] { }.ToObservableSequence();
                    }


                    var appRoute = status.Status.GetRoute();
                    return _appStatus.UpdateSystemStatusWithMeta(appRoute, status.Status, status.Meta).SelectMany(s => s);
                }
                catch (Exception e)
                {
                    return new IRxnQuestion[] { }.ToObservableSequence();
                }
            });
        }

        public IObservable<IRxn> Process(BasicErrorReport report)
        {
            return Rxn.Create<IRxn>(() =>
            {
                _appErrors.InsertError(report);
            });
        }
    }
}
