using System;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Ionic.Zip;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns.Hosting.Updates
{
    public class AppUpdateServiceClient : ReportsStatus, IUpdateServiceClient
    {
        private readonly IUpdateStorageClient _updateService;
        private readonly IFileSystemService _fileSystem;
        private readonly IRxnAppCfg _cfg;

        public AppUpdateServiceClient(IUpdateStorageClient updateService, IFileSystemService fileSystem, IRxnAppCfg forDownloadVersionCheck)
        {
            _fileSystem = fileSystem;
            _cfg = forDownloadVersionCheck;
            _updateService = updateService;
        }

        private string DeleteExistingAppIf(string system, string version, string destinationFolder, bool overwrite)
        {
            var existingCfg = Path.Combine(destinationFolder, "rxn.cfg");

            if (File.Exists(existingCfg) && RxnAppCfg.LoadCfg(existingCfg).Version.Equals(version))
            {
                $"App already @ {version}".LogDebug();

                return null;
            }

            if (_fileSystem.ExistsDirectory(destinationFolder))
            {
                if (overwrite)
                {
                    OnWarning("App already exists, overwriting");
                    _fileSystem.DeleteDirectory(destinationFolder);
                    return version;
                }
                else
                {
                    return null;
                }
            }

            return version;
        }

        public IObservable<string> GetOrCreateNextVersionAndFolder(string system, string version, string destinationFolder, bool overwrite)
        {
            return Rxn.Create<string>(o =>
            {
                if (version.IsNullOrWhiteSpace("Latest").Equals("Latest", StringComparison.OrdinalIgnoreCase))
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

        public IObservable<string> Download(string system, string v, string destinationFolder, IRxnAppCfg cfg = null, bool overwrite = true)
        {
            return Rxn.DfrCreate(() => GetOrCreateNextVersionAndFolder(system, v, destinationFolder, overwrite).SelectMany(version => 
            {
                if (version.IsNullOrWhitespace())//Already at the version we asked for
                {
                    OnVerbose("No new updates");
                    return version.ToObservable();
                }
                
                OnVerbose($"Downloading {system}@{version} to {destinationFolder}");
                var ms = new MemoryStream();
                
                return _updateService.GetUpdate(system, version)
                    .SelectMany(content => content.CopyToAsync(ms).ToObservable())
                    .Select(_ =>
                    {
                        ms.Seek(0, SeekOrigin.Begin);

                        OnVerbose("Extracting update to '{0}'", destinationFolder);

                        using (var contents = ZipFile.Read(ms))
                        {
                            contents.ExtractAll(destinationFolder, ExtractExistingFileAction.OverwriteSilently);
                        }
                        var osServices = new CrossPlatformOperatingSystemServices();
                        //spawn new app
                        
                        if(cfg != null)
                            cfg.Version = version;
                        
                        cfg?
                            .Save()
                            .Save(destinationFolder);
                
                        osServices.AllowToBeExecuted(cfg?.PathToSystemBinary);
                        
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
            dir = dir.TrimEnd(new char[] {'\\'});
            if (dir == ".")
            {
                dir = Environment.CurrentDirectory;
            }

            var dirname = new DirectoryInfo(dir).Name;
            var memoryStream = new MemoryStream();
            using (var zipFile = new ZipFile())
            {
                foreach (string pathToFile in _fileSystem.GetFiles(dir, searchPattern, true)
                    .Select(fm => fm.Fullname))
                {
                    var absolute = _fileSystem.GetDirectoryPart(pathToFile);
                    
                    var relative = absolute.Replace(dir.TrimEnd('/'), "").LogDebug(pathToFile);
                    if (relative.Equals("updates", StringComparison.OrdinalIgnoreCase))
                    {
                        "detetected nested update, skipping".LogDebug();
                        continue;
                    }
                    
                    if (!dir.EndsWith("/") && !dir.EndsWith("\\"))//use folder as index in .zip if no slash
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