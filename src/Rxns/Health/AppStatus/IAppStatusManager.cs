using System;
using Rxns.Interfaces;

namespace Rxns.Health.AppStatus
{
    public class SpareReactorAvailible : IRxn
    {
        public string Route { get; set; }
    }

    public interface IAppHeartBeatHandler
    {
        IObservable<IRxn> OnNewAppDiscovered(IAppStatusManager appStatus, SystemStatusEvent app, object[] meta);
        IObservable<IRxn> OnAppHeartBeat(IAppStatusManager appStatus,  SystemStatusEvent app, object[] meta);
    }

    public interface IAppStatusManager
    {
        IObservable<IRxnQuestion[]> UpdateSystemCommandIfOutofDate(SystemStatusEvent status);
        SystemStatusModel[] GetSystemStatus();
        IObservable<bool> UploadLogs(string tenantId, string systemName, IFileMeta file);

        IObservable<bool> UpdateSystemStatus(SystemStatusEvent status, params dynamic[] meta);

        IObservable<IRxnQuestion[]> UpdateSystemStatusWithMeta(string appRoute, SystemStatusEvent status, dynamic meta);
        dynamic GetSystemLog();
    }
}
