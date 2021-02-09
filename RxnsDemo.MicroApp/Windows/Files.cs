using System;
using System.IO;
using System.Reactive.Linq;
using Rxns;
using Rxns.Logging;

namespace Janison.MicroApp
{
    /// <summary>
    /// Extension methods of the Files class
    /// </summary>
    public static class Files
    {
        /// <summary>
        /// Compares two files for differences in their hash
        /// </summary>
        /// <param name="fileSource">The source file path</param>
        /// <param name="fileDestination">The destination file path</param>
        /// <returns>If the files are equal</returns>
        public static bool IsEqualsTo(this string fileSource, string fileDestination)
        {
            using (var f1 = new FileStream(fileSource, FileMode.Open))
            {
                using (var f2 = new FileStream(fileDestination, FileMode.Open))
                {
                    return f1.IsEquaTo(f2);
                }
            }
        }

        /// <summary>
        /// Using file-system paths, copies a file from one path to another using
        /// a specific buffer size to chunk data into during the copy process
        /// 
        /// Using WinIO for the the fastet copying under the hood
        /// </summary>
        /// <param name="sourceFile">The path to the file to copy</param>
        /// <param name="destinationFile">The path to the destination file</param>
        /// <param name="bufferSize">The size of the buffer used for the copy operation</param>
        public static void CopyTo(this string sourceFile, string destinationFile, int bufferSize = 65536)
        {
            var buffer = new byte[bufferSize];
            int bytesRead = 0;

            using (var source = new WinFileIO(buffer))
            {
                //create empty file to write too
                File.WriteAllText(destinationFile, String.Empty);

                //now do the copy using a shared buffer
                using (var destination = new WinFileIO(buffer))
                {
                    source.OpenForReading(sourceFile);
                    destination.OpenForWriting(destinationFile);

                    do
                    {
                        bytesRead = source.Read(bufferSize);
                        destination.WriteBlocks(bytesRead);
                    }
                    while (bytesRead == bufferSize);
                }
            }
        }

        /// <summary>
        /// Executes an action whenever a file or path has been 'changed'. This is a fault tolerant implementation of FileSystemWatcher
        /// Changes watched:
        /// -Changed
        /// -Created
        /// -Renamed
        /// </summary>
        /// <param name="path">The path to watch for changes</param>
        /// <param name="filter">The filter applied to the path ie *.txt</param>
        /// <param name="triggeredAction">The action to perform when the state changes</param>
        /// <returns>The object that watches for changes</returns>
        public static IDisposable WatchForChanges(string path, string filter, Action triggeredAction)
        {
            Func<IDisposable> startMonitor = null;
            FileSystemEventHandler updateAction = (o, e) => triggeredAction.Invoke();
            RenamedEventHandler renameAction = (o, e) => triggeredAction.Invoke();
            ErrorEventHandler errorAction = null;
            Action<FileSystemWatcher> restartMonitoring = null;

            Func<FileSystemWatcher, IDisposable> startMonitoring = (monitor) =>
            {
                errorAction = (sender, args) =>
                {
                    ReportStatus.Log.OnWarning("FSW => {0}".FormatWith(path), "Restarting because: {0}".FormatWith(args.GetException()));

                    Action retryUntilReady = null;
                    retryUntilReady = () => Observable.Timer(TimeSpan.FromSeconds(10)).Subscribe(_ =>
                    {
                        try
                        {
                            restartMonitoring(monitor);
                        }
                        catch (Exception e)
                        {
                            ReportStatus.Log.OnWarning("FSW => {0}".FormatWith(path), "Cannot restart yet because: {0}".FormatWith(e));
                            retryUntilReady();
                        }
                    });
                    retryUntilReady();
                };

                monitor.Changed += updateAction;
                monitor.Created += updateAction;
                monitor.Renamed += renameAction;
                monitor.Error += errorAction;
                monitor.EnableRaisingEvents = true;
                return new DisposableAction(() => monitor.Dispose());
            };

            Action<FileSystemWatcher> stopMonitoring = (monitor) =>
            {
                monitor.Changed -= updateAction;
                monitor.Created -= updateAction;
                monitor.Renamed -= renameAction;
                monitor.Error -= errorAction;
                monitor.Dispose();
            };

            restartMonitoring = (m) =>
            {
                stopMonitoring(m);
                startMonitoring(new FileSystemWatcher(path, filter));
                triggeredAction(); //trigger an onChanged event because while the path was inaccessible files may have changed
            };

            startMonitor = () =>
            {
                var monitor = new FileSystemWatcher(path, filter);
                return startMonitoring(monitor);
            };

            return startMonitor();
        }
    }
}
