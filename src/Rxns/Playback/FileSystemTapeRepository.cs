using System;
using System.Collections.Generic;
using System.Linq;
using Rxns.Interfaces;

namespace Rxns.Playback
{
    public class FileSystemTapeRepository : ITapeRepository
    {
        private readonly IFileSystemService _fileSystem;
        private readonly IStringCodec _defaultCodec;

        public FileSystemTapeRepository(IFileSystemService fileSystem, IStringCodec codec)
        {
            _defaultCodec = codec;
            _fileSystem = fileSystem;
        }

        public void Delete(string name)
        {
            _fileSystem.DeleteFile(name);
        }

        public ITapeStuff GetOrCreate(string fulleName, IStringCodec codec = null)
        {
            var directory = _fileSystem.GetDirectoryPart(fulleName);
            if (!directory.IsNullOrWhitespace() && !_fileSystem.ExistsDirectory(directory)) _fileSystem.CreateDirectory(directory);

            return RxnTape.FromSource(fulleName, new CapturedRxnTapeSource(TimeSpan.Zero, _fileSystem.GetOrCreateFile(fulleName), codec ?? _defaultCodec));
        }

        public IEnumerable<ITapeStuff> GetAll(string directory = @".\", string mask = "*.*", IStringCodec codec = null)
        {
            if (!directory.IsNullOrWhitespace() && !_fileSystem.ExistsDirectory(directory)) _fileSystem.CreateDirectory(directory);

            return _fileSystem.GetFiles(directory, mask, true)
                            .Select(f => RxnTape.FromSource(_fileSystem.PathCombine(directory, f.Name), new CapturedRxnTapeSource(TimeSpan.Zero, _fileSystem.GetOrCreateFile(f.Fullname), codec ?? _defaultCodec)));
        }
    }
}
