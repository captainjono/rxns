using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using Rxns.DDD.Commanding;
using Rxns.Hosting.Updates;
using Rxns.Interfaces;

namespace Rxns.Playback
{

    public class StartRecording : ServiceCommand
    {

    }

    public class StopRecording : ServiceCommand
    {

    }

    public class FileSystemTapeRepository : ITapeRepository, IServiceCommandHandler<StartRecording>, IServiceCommandHandler<StopRecording>
    {
        private readonly IFileSystemService _fileSystem;
        private readonly IAppStatusCfg _cfg;
        private readonly IStringCodec _defaultCodec;
        private ISubject<bool> _isStarted = new ReplaySubject<bool>(1);

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

            return RxnTape.FromSource(fileToGet, new CapturedRxnTapeSource(TimeSpan.Zero, _fileSystem.GetOrCreateFile(fileToGet), codec ?? _defaultCodec, _isStarted));
        }

        public IEnumerable<ITapeStuff> GetAll(string directory = @".\", string mask = "*.*", IStringCodec codec = null)
        {
            var rootedDir = Path.Combine(_cfg.AppRoot, directory);

            if (!directory.IsNullOrWhitespace() && !_fileSystem.ExistsDirectory(rootedDir)) _fileSystem.CreateDirectory(rootedDir);

            return _fileSystem.GetFiles(rootedDir, mask, true)
                            .Select(f => RxnTape.FromSource(_fileSystem.PathCombine(rootedDir, f.Name), new CapturedRxnTapeSource(TimeSpan.Zero, _fileSystem.GetOrCreateFile(f.Fullname), codec ?? _defaultCodec)));
        }

        public IObservable<CommandResult> Handle(StartRecording command)
        {
            return Rxn.Create(() => _isStarted.OnNext(true)).ToObservable().Select(_ => CommandResult.Success($"Recording activated").AsResultOf(command));
        }

        public IObservable<CommandResult> Handle(StopRecording command)
        {
            return Rxn.Create(() => _isStarted.OnNext(false)).ToObservable().Select(_ => CommandResult.Success($"Recording stopped").AsResultOf(command));
        }
    }
}
