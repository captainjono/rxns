using System;
using System.IO;
using Rxns.Commanding;
using Rxns.DDD.Commanding;
using Rxns.Interfaces;

namespace Rxns.Hosting.Updates
{
    public interface IAppUpdateManager : IServiceCommandHandler<UpdateSystemCommand>
    {
        /// <summary>
        /// Upload an update bundle for (systemName, version).
        /// </summary>
        /// <param name="knownSha">
        /// Optional pre-computed sha-256 of the bundle content. Forwarded to the
        /// storage backend so content-addressed stores (FileSystemAppUpdateRepo)
        /// can dedup uploads of byte-identical content under different versions.
        /// Server-side HTTP path sources this from the X-Content-Sha256 request
        /// header. Null disables the optimisation; the storage backend hashes
        /// the stream itself.
        /// </param>
        IObservable<IFileMeta> Upload(string systemName, string version, IFileMeta appUpdate, string knownSha = null);

        IObservable<Stream> GetUpdate(string systemName, string version);

        IObservable<AppUpdateInfo[]> AllUpdates(string systemName = null, int top = 3);

        /// <summary>Removes a specific update artifact. Returns true when the
        /// version was found and removed; false if it didn't exist.</summary>
        IObservable<bool> RemoveUpdate(string systemName, string version);

        IObservable<bool> PushUpdate(string systemName, string version, string username, string[] tenants);

        IObservable<AppUpdateStatus> SystemStatus { get; }
        void UploadLog(int logNumber = 0, bool truncate = false);
    }
}
