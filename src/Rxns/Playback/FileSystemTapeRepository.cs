using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Rxns.Hosting.Updates;
using Rxns.Interfaces;

namespace Rxns.Playback
{
    public class FileSystemTapeRepository : ITapeRepository
    {
        private readonly IFileSystemService _fileSystem;
        private readonly IAppStatusCfg _cfg;
        private readonly IStringCodec _defaultCodec;

        public FileSystemTapeRepository(IFileSystemService fileSystem, IAppStatusCfg cfg, IStringCodec codec)
        {
            _defaultCodec = codec;
            _fileSystem = fileSystem;
            _cfg = cfg;
        }

        public void Delete(string name)
        {
            _fileSystem.DeleteFile(name);
        }

        public ITapeStuff GetOrCreate(string fulleName, IStringCodec codec = null)
        {
            var fileToGet = Path.Combine(_cfg.AppRoot, fulleName);
            var directory = _fileSystem.GetDirectoryPart(fileToGet);
            if (!directory.IsNullOrWhitespace() && !_fileSystem.ExistsDirectory(directory)) _fileSystem.CreateDirectory(directory);

            return RxnTape.FromSource(fileToGet, new CapturedRxnTapeSource(TimeSpan.Zero, _fileSystem.GetOrCreateFile(fileToGet), codec ?? _defaultCodec));
        }

        public IEnumerable<ITapeStuff> GetAll(string directory = @".\", string mask = "*.*", IStringCodec codec = null)
        {
            var rootedDir = Path.Combine(_cfg.AppRoot, directory);

            if (!directory.IsNullOrWhitespace() && !_fileSystem.ExistsDirectory(rootedDir)) _fileSystem.CreateDirectory(rootedDir);

            return _fileSystem.GetFiles(rootedDir, mask, true)
                            .Select(f => RxnTape.FromSource(_fileSystem.PathCombine(rootedDir, f.Name), new CapturedRxnTapeSource(TimeSpan.Zero, _fileSystem.GetOrCreateFile(f.Fullname), codec ?? _defaultCodec)));
        }
    }
}
