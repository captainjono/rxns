using System;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using Rxns.DDD.CQRS;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns.Hosting.Updates
{

    public class FileSystemAppUpdateRepo : IUpdateStorageClient
    {
        private readonly AppUpdateCfg _cfg;
        private readonly IFileSystemService _fs;

        public FileSystemAppUpdateRepo(IFileSystemService fs)
        {
            _cfg = new AppUpdateCfg() {NumberOfRollingAppUpdates = 3};
            _fs = fs;
        }

        public IObservable<bool> CreateUpdate(string systemName, string version, Stream update)
        {
            return Rxn.Create(() =>
                {
                    var path = _fs.PathCombine("updates");

                    if (!_fs.ExistsDirectory(path))
                        _fs.CreateDirectory(path);

                    update.Seek(0, SeekOrigin.Begin);
                    using (var file =
                        _fs.CreateWriteableFile(_fs.PathCombine(path, GetUpdateName(systemName, version))))
                        update.CopyTo(file);

                    return true;
                })
                .Do(_ => TruncateUpdatesIf(systemName));
        }

        private void TruncateUpdatesIf(string systemName)
        {
            ListUpdates(systemName, 100).Do(all =>
            {
                var total = all.Length;
                var oldest = all.LastOrDefault();

                while (total > _cfg.NumberOfRollingAppUpdates)
                {
                    DeleteUpdate(systemName, all[--total].Version.LogDebug("ROLLING DELETE"));
                }
            }).Until();
        }

        private void DeleteUpdate(string systemName, string version)
        {
            _fs.DeleteFile(_fs.PathCombine("updates", $"{systemName}-{version}.zip"));
        }


        private string GetUpdateName(string systemName, string version)
        {
            return $"{systemName}-{version}.zip";
        }

        public IObservable<Stream> GetUpdate(string systemName, string version = null)
        {
            return Rxn.Create(() => _fs.GetReadableFile(_fs.PathCombine("updates", GetUpdateName(systemName, version))))
                .Catch<Stream, Exception>(e =>
                {
                    ReportStatus.Log.OnWarning($"While downloading update {e}");
                    throw new DomainCommandException(String.Format("Could not find '{0}' version '{1}'", systemName,
                        version));
                });
        }


        public IObservable<AppUpdateInfo[]> ListUpdates(string systemName, int top = 3)
        {
            if (!_fs.ExistsDirectory(_fs.PathCombine("updates")))
                return Rxn.Empty<AppUpdateInfo[]>();

            return _fs.GetFiles("updates",
                systemName.IsNullOrWhitespace("all").Equals("all", StringComparison.OrdinalIgnoreCase)
                    ? "*.zip"
                    : $"{systemName}-*.zip").OrderByDescending(f => f.LastWriteTime).Take(top).Select(f =>
            {
                var withReactorName = f.Name;
                var version = withReactorName.Substring(withReactorName.IndexOf('-') + 1,
                    withReactorName.Length - withReactorName.IndexOf('-') - 1).Replace(".zip", "");


                return new AppUpdateInfo()
                {
                    SystemName = withReactorName.Substring(0, withReactorName.IndexOf('-')),
                    Version = version
                };
            }).ToObservableSequence().ToArray().Catch<AppUpdateInfo[], Exception>(_ => Rxn.Empty<AppUpdateInfo[]>());
        }
    }
}
