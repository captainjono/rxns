using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Ionic.Zip;
using Rxns.DDD.Commanding;
using Rxns.DDD.CQRS;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns.Hosting.Updates
{
   

    public class AppUpdateServiceClient : ReportsStatus, IUpdateServiceClient
    {
        private readonly IUpdateStorageClient _updateService;
        private readonly IFileSystemService _fileSystem;
        private readonly IStoreAppUpdates _cmdService;
        private readonly IRxnAppCfg _cfg;

        public AppUpdateServiceClient(IUpdateStorageClient updateService, IFileSystemService fileSystem, IStoreAppUpdates cmdService, IRxnAppCfg forDownloadVersionCheck)
        {
            _fileSystem = fileSystem;
            _cmdService = cmdService;
            _cfg = forDownloadVersionCheck;
            _updateService = updateService;
        }

        private string DeleteExistingAppIf(string system, string version, string destinationFolder, bool overwrite)
        {
            _cmdService.Run(new PrepareForAppUpdate(system, version, overwrite, destinationFolder));

            return version;
        }

        public IObservable<string> CheckForNewVersionAndCreateFolderForIt(string system, string version, string destinationFolder, bool overwrite)
        {
            //todo: update, need create dir {system}%%{version}
            return Rxn.Create<string>(o =>
            {
                if (version.IsNullOrWhiteSpace("Latest").BasicallyEquals("Latest"))
                {
                    return _updateService.ListUpdates(system).Select(a => a.FirstOrDefault()).Select(latest =>
                    {
                        if (latest.Version.IsNullOrWhitespace())
                        {
                            throw new Exception($"'{system}' not found on update server");
                        }

                        return DeleteExistingAppIf(system, latest.Version, destinationFolder, overwrite);
                    }).Subscribe(o);
                }

                return Rxn.Create(() => DeleteExistingAppIf(system, version, destinationFolder, overwrite)).Subscribe(o);
            });
        }

        public IObservable<string> KeepUpdated(string systemName, string version, string destinationFolderRoot, IRxnAppCfg cfg = null, bool overwrite = true)
        {
            return TimeSpan.FromSeconds(30)
                .Then()
                .SelectMany(_ => Download(systemName, version, destinationFolderRoot, cfg, overwrite))
                //untested - need to implement inside of tests also
                ;
        }

        public IObservable<string> Download(string system, string v, string destinationFolder = null, IRxnAppCfg cfg = null, bool overwrite = true)
        {
            return Rxn.DfrCreate(() => CheckForNewVersionAndCreateFolderForIt(system, v, destinationFolder, overwrite).SelectMany(version =>
            {
                if (version.IsNullOrWhitespace())//Already at the version we asked for
                {
                    OnVerbose("No new updates");
                    return string.Empty.ToObservable();
                }

                var targetPath = _cmdService.Run(new GetAppDirectoryForAppUpdate(system, v, destinationFolder)).WaitR();
                OnVerbose($"Downloading {system}@{version} to {targetPath}");
                var ms = new MemoryStream();
                
                return _updateService.GetUpdate(system, version)
                    .SelectMany(content => content.CopyToAsync(ms).ToObservable())
                    .Select(_ =>
                    {
                        ms.Seek(0, SeekOrigin.Begin);

                        OnVerbose("Extracting update to '{0}'", targetPath);

                        using (var contents = ZipFile.Read(ms))
                        {
                            contents.ExtractAll(targetPath, ExtractExistingFileAction.OverwriteSilently);
                        }
                        //spawn new app

                        var targetCfg = cfg ?? RxnAppCfg.Detect(_cfg.Args);
                        targetCfg.Version = version;

                        targetCfg
                            .Save()
                            .Save(targetPath);

                        if (!cfg?.AppPath.IsNullOrWhitespace() ?? false)
                        {
                            //todo fix hard ref
                            new CrossPlatformOperatingSystemServices().AllowToBeExecuted(cfg?.AppPath);
                        }

                        return version;
                    })
                    .Finally(() => ms.Dispose());
            }));
        }

        public IObservable<Unit> Upload(string system, string version, string sourceFolder)
        {
            return Rxn.Create(() =>
            {
                OnVerbose("Uploading update for: {1} ({2} - '{0}')", sourceFolder, system, version);

                
                var zippedUpdate = Zip(sourceFolder, "*.*");
                return _updateService.CreateUpdate(system, version, zippedUpdate).Select(_ => new Unit()).FinallyR(() =>
                {
                    OnVerbose($"Upload of {zippedUpdate.Length.ToFileSize()} complete");
                    zippedUpdate.Dispose();
                });
            });
        }


        public Stream Zip(string dir, string searchPattern = "*.*")
        {
            if (dir == ".")
            {
                dir = Environment.CurrentDirectory;
            }

            var dirname = new DirectoryInfo(dir).Name;
            var memoryStream = new MemoryStream();
            //use folder as index in .zip if no slash
            var shouldNest = !(dir.EndsWith("/") || !dir.EndsWith("\\"));

            using (var zipFile = new ZipFile())
            {
                foreach (string pathToFile in _fileSystem.GetFiles(dir.TrimEnd('/', '\\'), searchPattern, true)
                    .Select(fm => fm.Fullname))
                {
                    var absolute = _fileSystem.GetDirectoryPart(pathToFile);
                    
                    var relative = absolute.Replace(dir.TrimEnd('/', '\\'), "").LogDebug(pathToFile);
                    if (relative.Equals("updates", StringComparison.OrdinalIgnoreCase) || relative.BasicallyContains("%%"))
                    {
                        //todo make detection more formal/optional
                        "detected nested update, skipping".LogDebug();
                        continue;
                    }

                    if (shouldNest)
                    {
                        relative = $"{dirname}{relative}";
                    }

                    zipFile.AddFile(pathToFile, relative);
                }

                zipFile.Save(memoryStream);
            }

            memoryStream.Seek(0L, SeekOrigin.Begin);
            return memoryStream;
        }
    }
}