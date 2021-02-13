using System;
using Rxns.DDD.Commanding;
using Rxns.Interfaces;

namespace Rxns.Health.AppStatus
{
    public class SpareReactorAvailible : IRxn
    {
        public string Route { get; set; }
    }

    public interface IAppStatusManager
    {

        SystemStatusModel[] GetSystemStatus();
        IObservable<bool> UploadLogs(string tenantId, string systemName, IFileMeta file);

        IObservable<bool> UpdateSystemStatus(SystemStatusEvent status, params dynamic[] meta);

        IObservable<RxnQuestion[]> UpdateSystemStatusWithMeta(string appRoute, SystemStatusEvent status, dynamic meta);
        dynamic GetSystemLog();
    }
}
