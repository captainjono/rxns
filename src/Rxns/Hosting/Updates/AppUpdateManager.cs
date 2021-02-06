using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Rxns.Collections;
using Rxns.Commanding;
using Rxns.DDD.Commanding;
using Rxns.DDD.CQRS;
using Rxns.Health;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns.Hosting.Updates
{
    public enum AppUpdateStatus
    {
        Idle, 
        Updating,
        Update,
        UploadLog
    }

    public class NewAppVersionReleased : IRxn
    {
        public string SystemName { get; set;  }
        public string Version { get; set; }
    }

    public class LocalAppUpdateServer : ReportsStatus, IAppUpdateManager, IServiceCommandHandler<UpdateSystemCommand>, IRxnPublisher<IRxn>
    {
        private readonly ICommandService _cmdHub;
        private readonly IRxnAppInfo _systemInfo;
        private readonly IFileSystemService _fileSystem;
        private readonly IZipService _zipService;
        private readonly IAppStatusServiceClient _appStatus;
        private readonly IRxnHostableApp _bootstrap;
        private readonly IUpdateStorageClient _client;
        private readonly BehaviorSubject<AppUpdateStatus> _onStateChanged = new BehaviorSubject<AppUpdateStatus>(AppUpdateStatus.Idle);
        private readonly IUpdateServiceClient _updateService;
        private Action<IRxn> _pubish;

        public LocalAppUpdateServer(IUpdateStorageClient client, IUpdateServiceClient updateService, ICommandService cmdHub, IRxnAppInfo systemInfo, IFileSystemService fileSystem, IZipService zipService, IAppStatusServiceClient appStatus, IRxnHostableApp bootstrap)
        {
            _client = client;
            _updateService = updateService;
            _cmdHub = cmdHub;
            _systemInfo = systemInfo;
            _fileSystem = fileSystem;
            _zipService = zipService;
            _appStatus = appStatus;
            _bootstrap = bootstrap;
        }

        public IObservable<AppUpdateStatus> SystemStatus
        {
            get { return _onStateChanged; }
        }
        
        public IObservable<IFileMeta> Upload(string systemName, string version, IFileMeta appUpdate)
        {
            if (!appUpdate.Name.ToLower().Contains("zip"))
                throw new ArgumentException("Update packages must be a zip file");

            OnInformation("Uploading version '{0}' for '{1}'", version, systemName);

            var update = appUpdate.Contents;

            return _client.CreateUpdate(systemName, version, update)
                .FinallyR(() =>
                {
                    update.Dispose();
                })
                .Select(isSuccess =>
                {
                    if (isSuccess)
                        OnVerbose("'{0}' received for update '{1}''", appUpdate.Length.ToFileSize(), version);
                    else
                        OnWarning("Upload update failed for '{0}'", version);

                    _pubish(new NewAppVersionReleased()
                    {
                        SystemName = systemName,
                        Version = version
                    });

                    return appUpdate;
                });
        }

        public IObservable<Stream> GetUpdate(string systemName, string version)
        {
            return Rxn.DfrCreate<Stream>(() => Rxn.Create<Stream>(o =>
            {
                if (version.Equals("Latest", StringComparison.OrdinalIgnoreCase))
                {
                    return AllUpdates(systemName, 1).SelectMany(latest =>
                    {
                        if (latest == null)
                        {
                            throw new Exception($"No updates for {systemName}");
                        }
                        
                        return _client.GetUpdate(systemName, latest.FirstOrDefault()?.Version).Select(update =>
                        {
                            update.Seek(0, SeekOrigin.Begin);
                            return update;
                        });
                    }).Subscribe(o);
                }
                version = version.Replace(".zip", "");
                OnInformation("Getting update '{0}' for '{1}", version, systemName);

                return _client.GetUpdate(systemName, version).Select(update =>
                {
                    update.Seek(0, SeekOrigin.Begin);
                    return update;
                }).Subscribe(o);
            }));
            
        }

        public IObservable<AppUpdateInfo[]> AllUpdates(string systemName = null, int top = 3)
        {
            OnInformation("Retrieving list of last '{0}' updates for '{1}'", top, systemName);

            return _client.ListUpdates(systemName, top);
        }

        public IObservable<bool> PushUpdate(string systemName, string version, string username, string[] tenants)
        {
            return Rxn.Create(() =>
            {

                if (tenants == null || tenants.Length == 0)
                {
                    OnWarning("Bad update request received from: {0} ({1})", username, tenants);
                    return false;
                }

                foreach (var cmd in tenants.Select(tenant => RxnQuestion.ForTenant<UpdateSystemCommand>(tenant, systemName)))
                {
                    cmd.Options = version;
                    _cmdHub.Run(cmd);
                }

                OnInformation(@"Update '{0}\{1}' pushed too: {2}", systemName, version, tenants.ToStringEach());
                return true;
            });
        }


        public IObservable<CommandResult> Handle(UpdateSystemCommand cmd)
        {
            return DownloadAndUpdate(cmd.SystemName, cmd.Version);
        }

        public void UploadLog(int logNumber = 0, bool truncate = false)
        {
            try
            {
                SetState(AppUpdateStatus.UploadLog);

                var logFileIndex = DateTime.Now.AddDays(logNumber * -1);
                var logFile = _fileSystem.GetFiles("logs", "*.log").FirstOrDefault(l => l.LastWriteTime.Date == logFileIndex.Date);

                if (logFile == null)
                    throw new ArgumentException(String.Format("Cannot find a log from the {0}",
                        logFileIndex.ToShortDateString()));

                var zipFile = _zipService.Zip(new[] { logFile });

                if (truncate)
                    _fileSystem.DeleteFile(logFile.Fullname);

                using (zipFile)
                    _appStatus.PublishLog(zipFile).Wait();
            }
            finally
            {
                SetState(AppUpdateStatus.Idle);
            }
        }

        private IObservable<CommandResult> UploadLog(string parameters)
        {
            return Rxn.Create<CommandResult>(o =>
            {
                if (parameters.IsNullOrWhitespace())
                    parameters = "0 truncate";

                var @params = parameters.Split(' ');

                var requestedLog = @params[0];
                var truncateRequested = @params.Length > 0
                    ? @params[0] == "truncate"
                    : @params.Length > 1 && @params[1] == "truncate";

                int logNumber = 0;
                //the log file index, 0 based, with 1 being today, 2 being yesterday etc.
                try
                {
                    logNumber = requestedLog.AsInt();
                }
                catch (Exception)
                {
                    //ignore
                }

                UploadLog(logNumber, truncateRequested);
                o.OnNext(CommandResult.Success());
                o.OnCompleted();

                return Disposable.Empty;
            });
        }


        private IObservable<CommandResult> DeleteUpdate(string version)
        {
            return Rxn.Create<CommandResult>(() =>
            {
                //make sure the version makes sense
                if (version.IsNullOrWhitespace()) throw new ArgumentNullException("version");

                if (_systemInfo.Version.Equals(version, StringComparison.InvariantCultureIgnoreCase))
                    throw new ArgumentException("Cannot delete current version of app");

                var destination = _bootstrap.GetDirectoryForVersion(version);
                _fileSystem.DeleteDirectory(destination);
            });
        }

        private IObservable<CommandResult> DownloadAndUpdate(string systemName, string version)
        {
            if (_onStateChanged.Value() != AppUpdateStatus.Idle)
            {
                return CommandResult.Failure("Update already in progress").ToObservable();
            }
            
            return Rxn.Create<CommandResult>(o =>
            {
                if (String.IsNullOrWhiteSpace(version)) throw new ArgumentNullException("version");
                if (_systemInfo.Name.Equals(systemName, StringComparison.InvariantCultureIgnoreCase) && _systemInfo.Version.Equals(version, StringComparison.InvariantCultureIgnoreCase))
                    throw new ArgumentException("cannot update to same version");

                try
                {
                    SetState(AppUpdateStatus.Update);

                    var destination = _bootstrap.GetDirectoryForVersion(version);
                    OnVerbose("Using '{0}' for update", destination);

                    //see if the update is already installed
                    if (_fileSystem.ExistsDirectory(destination))
                    {
                        OnWarning("Update already exists");
                        if (_fileSystem.GetFiles(destination, "*.dll").Any())
                        {
                            SetState(AppUpdateStatus.Idle);
                            o.OnNext(CommandResult.Success());
                            o.OnCompleted();
                            _bootstrap.MigrateTo(systemName, version);
                            return Disposable.Empty;
                        }
                    }

                    return _updateService.Download(systemName, version, destination)
                                                .Finally(() => o.OnCompleted())
                                                .Subscribe(this, _ =>
                                                {
                                                    OnInformation("Restarting system to version '{0}'", version);
                                                    o.OnNext(CommandResult.Success());
                                                    o.OnCompleted();
                                                    _bootstrap.MigrateTo(systemName, version).WaitR();
                                                },
                                                onError: (e, error) =>
                                                {
                                                    o.OnError(error);
                                                },
                                                onDisposing: _ =>
                                                {
                                                    SetState(AppUpdateStatus.Idle);
                                                });
                }
                catch (Exception)
                {
                    SetState(AppUpdateStatus.Idle);
                    throw;
                }
            });
        }



        private void SetState(AppUpdateStatus state)
        {
            _onStateChanged.OnNext(state);
        }

        public void ConfigiurePublishFunc(Action<IRxn> publish)
        {
            _pubish = publish;
        }
    }
}
