using System;
using System.IO;
using System.Reactive;
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
        public static IObservable<string> WatchForChanges(string path, string filter, bool triggerOnChanged = true, bool triggerOnCreated = true, bool triggerOnRenamed = true)
        {

            return Rxn.Create<string>(o =>
            {
                Func<IDisposable> startMonitor = null;
                FileSystemEventHandler updateAction = (_, e) => o.OnNext(e.FullPath);
                RenamedEventHandler renameAction = (_, e) => o.OnNext(e.FullPath);
                ErrorEventHandler errorAction = null;
                Action<FileSystemWatcher> restartMonitoring = null;

                Func<FileSystemWatcher, IDisposable> startMonitoring = (monitor) =>
                {
                    errorAction = (sender, args) =>
                    {
                        ReportStatus.Log.OnWarning("FSW => {0}".FormatWith(path),
                            "Restarting because: {0}".FormatWith(args.GetException()));

                        Action retryUntilReady = null;
                        retryUntilReady = () => Rxn.Create(TimeSpan.FromSeconds(10)).Subscribe(_ =>
                        {
                            try
                            {
                                restartMonitoring(monitor);
                            }
                            catch (Exception e)
                            {
                                ReportStatus.Log.OnWarning("FSW => {0}".FormatWith(path),
                                    "Cannot restart yet because: {0}".FormatWith(e));
                                retryUntilReady();
                            }
                        });
                        retryUntilReady();
                    };

                    if (triggerOnChanged)
                        monitor.Changed += updateAction;

                    if (triggerOnCreated)
                        monitor.Created += updateAction;

                    if (triggerOnRenamed)
                        monitor.Renamed += renameAction;

                    monitor.Error += errorAction;
                    monitor.EnableRaisingEvents = true;

                    return new DisposableAction(() => monitor.Dispose());
                };

                Action<FileSystemWatcher> stopMonitoring = (monitor) =>
                {

                    if (triggerOnChanged)
                        monitor.Changed -= updateAction;

                    if (triggerOnCreated)
                        monitor.Created -= updateAction;

                    if (triggerOnRenamed)
                        monitor.Renamed -= renameAction;

                    monitor.Error -= errorAction;
                    monitor.Dispose();
                };

                restartMonitoring = (m) =>
                {
                    stopMonitoring(m);
                    startMonitoring(new FileSystemWatcher(path, filter));
                    o.OnNext(path); //trigger an onChanged event because while the path was inaccessible files may have changed
                };

                startMonitor = () =>
                {
                    var monitor = new FileSystemWatcher(path, filter);
                    return startMonitoring(monitor);
                };

                return startMonitor();
            })
            //https://stackoverflow.com/questions/1764809/filesystemwatcher-changed-event-is-raised-twice/55100612
            .BufferFirstLast(TimeSpan.FromSeconds(1), true, false);
        }

    }
}
