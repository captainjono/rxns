using System;
using System.IO;
using Janison.Micro;
using Rxns.Logging;

namespace Rxns.Windows
{
    /// <summary>
    /// Extension methods of the Files class
    /// </summary>
    public static class Files
    {
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
                    GeneralLogging.Log.OnWarning("FSW => {0}".FormatWith(path), "Restarting because: {0}".FormatWith(args.GetException()));

                    Action retryUntilReady = null;
                    retryUntilReady = () => Rxn.Create(TimeSpan.FromSeconds(10)).Subscribe(_ =>
                    {
                        try
                        {
                            restartMonitoring(monitor);
                        }
                        catch (Exception e)
                        {
                            GeneralLogging.Log.OnWarning("FSW => {0}".FormatWith(path), "Cannot restart yet because: {0}".FormatWith(e));
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
