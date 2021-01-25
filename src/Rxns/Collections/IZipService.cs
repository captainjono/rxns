using System;
using Rxns.Interfaces;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using Ionic.Zip;

namespace Rxns.Collections
{
    public interface IZipService
    {
        Stream Zip(params IFileMeta[] files);

        Stream Zip(string directory, string searchPattern = "*.*");

        IObservable<IFileMeta> GetFiles(string zipFile, string searchPattern = null);
    }

    public class NoZipService : IZipService
    {
        public Stream Zip(params IFileMeta[] files)
        {
            throw new NotImplementedException();
        }

        public Stream Zip(string directory, string searchPattern = "*.*")
        {
            throw new NotImplementedException();
        }

        public IObservable<IFileMeta> GetFiles(string zipFile, string searchPattern = null)
        {
            throw new NotImplementedException();
        }
    }

    public class ZipService : IZipService
    {
        private readonly IFileSystemService _fileSystem;

        public ZipService(IFileSystemService fileSystem)
        {
            this._fileSystem = fileSystem;
        }

        public Stream Zip(params IFileMeta[] files)
        {
            var zipFile = new ZipFile();
            var memoryStream = new MemoryStream();
            foreach (IFileMeta file in files)
                zipFile.AddEntry(file.Fullname, file.Contents);
            zipFile.Save((Stream) memoryStream);
            memoryStream.Seek(0L, SeekOrigin.Begin);
            return (Stream) memoryStream;
        }

        public Stream Zip(string dir, string searchPattern = "*.*")
        {
            var memoryStream = new MemoryStream();
            var zipFile = new ZipFile();
            foreach (string pathToFile in this._fileSystem.GetFiles(dir, searchPattern, true)
                .Select<IFileMeta, string>((Func<IFileMeta, string>) (fm => fm.Fullname)))
                zipFile.AddFile(pathToFile, this._fileSystem.GetDirectoryPart(pathToFile).Replace(dir, ""));
            zipFile.Save((Stream) memoryStream);
            memoryStream.Seek(0L, SeekOrigin.Begin);
            return (Stream) memoryStream;
        }

        public IObservable<IFileMeta> GetFiles(string zipFile, string fileMask = null)
        {
            var getFilesFrom = ZipFile.Read(this._fileSystem.GetReadableFile(zipFile));
            return (fileMask != null ? 
                getFilesFrom.Where(f => f.FileName.Contains(fileMask, StringComparison.OrdinalIgnoreCase)) : 
                getFilesFrom.Entries).Select(f => new InMemoryFile()
                {
                    Fullname = f.FileName,
                    Contents = (Stream) f.OpenReader(),
                    LastWriteTime = f.LastModified
                }).ToObservable();
        }
    }
}
