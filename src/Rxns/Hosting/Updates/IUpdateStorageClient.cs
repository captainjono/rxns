using System;
using System.Collections.Generic;
using System.IO;
using System.Reactive;
using System.Threading.Tasks;

namespace Rxns.Hosting.Updates
{
    public class AppUpdateCfg
    {
        public int NumberOfRollingAppUpdates { get; set; }
    }

    /// <summary>
    /// Represents the storage mechanism for system updates
    /// </summary>
    public interface IUpdateStorageClient
    {
        /// <summary>
        /// Uploads an update for a system
        /// </summary>
        /// <param name="systemName">The system the update applies too</param>
        /// <param name="version">The version number of the update</param>
        /// <param name="update">The update</param>
        /// <returns>If the update was created or not</returns>
        IObservable<bool> CreateUpdate(string systemName, string version, Stream update);

        /// <summary>
        /// Gets a specific update for a system
        /// </summary>
        /// <param name="systemName">The system name</param>
        /// <param name="version">The update version</param>
        /// <returns></returns>
        IObservable<Stream> GetUpdate(string systemName, string version = null);

        /// <summary>
        /// Lists all the availble updates for a system in order of newest update
        /// </summary>
        /// <param name="systemName">The system to query</param>
        /// <param name="top">The max number of updates returned</param>
        /// <returns>A list of updates, not lazily evaluated</returns>
        IObservable<AppUpdateInfo[]> ListUpdates(string systemName, int top = 3);
    }

    public interface IUpdateServiceClient
    {
        /// <summary>
        /// Downloads a system at a specific version, installing it into the destination folder.
        /// If the destinaiton already exists, and is at the correct verison, this process becomes a no-op
        /// and false is returned to indicate the system was already up-to-date.
        /// </summary>
        /// <param name="system"></param>
        /// <param name="version"></param>
        /// <param name="destinationFolder"></param>
        /// <param name="overwrite"></param>
        /// <returns></returns>
        IObservable<string> Download(string system, string version, string destinationFolder, IRxnAppCfg cfg = null, bool overwrite = true);
        IObservable<Unit> Upload(string system, string version, string sourceFolder);
    }
}
