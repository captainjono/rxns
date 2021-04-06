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

        IObservable<IFileMeta> GetFiles(Stream zipFile, string fileMask = null);
        IObservable<IFileMeta> GetFiles(string zipFile, string fileMask = null);
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

        public IObservable<IFileMeta> GetFiles(Stream zipFile, string fileMask = null)
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

        public IObservable<IFileMeta> GetFiles(Stream zipFile, string fileMask = null)
        {
            var getFilesFrom = ZipFile.Read(zipFile);
            return (fileMask != null ?
                getFilesFrom.Where(f => f.FileName.Contains(fileMask, StringComparison.OrdinalIgnoreCase)) :
                getFilesFrom.Entries).Where(f => !f.FileName.IsNullOrWhitespace()).Select(f => new JitFile()
            {
                Fullname = f.FileName,
                GetContents =  () =>
                {
                    var s = new MemoryStream();
                    f.Extract(s);
                    s.Position = 0;
                    return s;
                },
                LastWriteTime = f.LastModified,
                Name = new FileInfo(f.FileName).Name
                }).ToObservable();
        }


        public IObservable<IFileMeta> GetFiles(string zipFile, string fileMask = null)
        {
            return GetFiles(this._fileSystem.GetReadableFile(zipFile), fileMask);
        }
    }

    public class JitFile : IFileMeta
    {
        public Stream Contents
        {
            get => GetContents();
            set => throw new NotImplementedException();
        }

        public string ContentType { get; set; }
        public string Fullname { get; set; }
        public string Hash { get; set; }
        public DateTime LastWriteTime { get; set; }
        public long Length { get; set; }
        public string Name { get; set;  }

        public Func<Stream> GetContents { get; set; }
    }
}
