using System;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using Ionic.Zip;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns.Hosting.Updates
{
    public class AppUpdateServiceClient : ReportsStatus, IUpdateServiceClient
    {
        private readonly IUpdateStorageClient _updateService;
        private readonly IFileSystemService _fileSystem;
        public AppUpdateServiceClient(IUpdateStorageClient updateService, IFileSystemService fileSystem)
        {
            _fileSystem = fileSystem;
            _updateService = updateService;
        }

        public IObservable<Unit> Download(string system, string version, string destinationFolder, bool overwrite = true)
        {
            return Rxn.Create(() =>
            {
                OnVerbose("Downloading new version '{0}'", version);

                return _updateService.GetUpdate(system, version)
                                    .Select(content =>
                                    {
                                        
                                        if (_fileSystem.ExistsDirectory(destinationFolder))
                                        {
                                            if (!overwrite) return new Unit();

                                            OnWarning("Update already exists, overwriting");
                                            _fileSystem.DeleteDirectory(destinationFolder);
                                        }

                                        using (var ms = new MemoryStream())
                                        {
                                            content.CopyToAsync(ms).Wait(); //todo: fix blocking code, not a perf path but still!
                                            ms.Seek(0, SeekOrigin.Begin);

                                            OnVerbose("Extracting update to '{0}'", destinationFolder);

                                            using (var contents = ZipFile.Read(ms))
                                            {
                                                contents.ExtractAll(destinationFolder);
                                            }
                                        }

                                        //if (!_fileSystem.GetFiles(destinationFolder, "*.dll").Any())
                                        //{
                                        //    throw new InvalidDataException("Update is corrupted because no .dll files can be found");
                                        //}

                                        return new Unit();
                                    });
            });
        }

        public IObservable<Unit> Upload(string system, string version, string sourceFolder)
        {
            return Rxn.Create(() =>
            {
                OnVerbose("Uploading update for: {1} ({2} - '{0}')", sourceFolder, system, version);

                var zippedUpdate = Zip(sourceFolder);
                return _updateService.CreateUpdate(system, version, zippedUpdate).Select(_ => new Unit()).FinallyR(() =>
                {
                    OnVerbose($"Upload of {zippedUpdate.Length.ToFileSize()} complete");
                    zippedUpdate.Dispose();
                });
            });
        }


        public Stream Zip(string dir, string searchPattern = "*.*")
        {
            dir = dir.TrimEnd(new char[] { '\\' });

            var memoryStream = new MemoryStream();
            using(var zipFile = new ZipFile())
            {
                foreach (string pathToFile in this._fileSystem.GetFiles(dir, searchPattern, true).Select(fm => fm.Fullname))
                {
                    
                    var absolute = _fileSystem.GetDirectoryPart(pathToFile);
                    var relative = absolute.Replace(dir, "").LogDebug(pathToFile);
                    if (relative.Equals("updates", StringComparison.OrdinalIgnoreCase))
                    {
                        "detetected nested update, skipping".LogDebug();
                        continue;
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
