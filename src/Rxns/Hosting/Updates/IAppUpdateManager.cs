using System;
using System.IO;
using Rxns.Interfaces;

namespace Rxns.Hosting.Updates
{
    public interface IAppUpdateManager
    {
        IObservable<IFileMeta> Upload(string systemName, string version, IFileMeta appUpdate);

        IObservable<Stream> GetUpdate(string systemName, string version);

        IObservable<AppUpdateInfo[]> AllUpdates(string systemName = null, int top = 3);

        IObservable<bool> PushUpdate(string systemName, string version, string username, string[] tenants);

        IObservable<AppUpdateStatus> SystemStatus { get; }
    }
}
