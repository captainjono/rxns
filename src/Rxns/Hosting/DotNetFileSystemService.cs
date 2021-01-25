using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive;
using Janison.Micro;
using Rxns.Interfaces;
using Rxns.Windows;

namespace Rxns.Hosting
{
    /// <summary>
    /// This service provides access to windows file system operations
    /// </summary>
    public class DotNetFileSystemService : IFileSystemService
    {
        public void CreateDirectory(string path)
        {
            Directory.CreateDirectory(path);
        }

        public string GetFilePart(string pathToFile)
        {
            var fi = new FileInfo(pathToFile);

            return fi.Name;
        }

        public string GetDirectoryPart(string pathToFile)
        {
            var fi = new FileInfo(pathToFile);

            return fi.DirectoryName;
        }

        public IFileMeta GetOrCreateFile(string path)
        {
            return new ReadWriteFileMeta() { Fullname = path};
        }

        public string PathCombine(params string[] paths)
        {
            return Path.Combine(paths ?? new string[]{});
        }

        public void Copy(string source, string destination, int buffer)
        {
            File.Copy(source, destination);
        }

        public bool AreEqual(string source, string destination)
        {
            return source.ToLower() == destination.ToLower();
        }

        public void DeleteFile(params string[] destination)
        {
            File.Delete(Path.Combine(destination));
        }

        public void DeleteDirectory(params string[] destination)
        {
            Directory.Delete(Path.Combine(destination), true);
        }

        public bool ExistsFile(string file)
        {
            return File.Exists(file);
        }

        public bool ExistsDirectory(string path)
        {
            return Directory.Exists(path);
        }

        public bool IsNewer(string fileA, string fileB)
        {
            return File.GetLastAccessTime(fileA) > File.GetLastAccessTime(fileB) || File.GetCreationTime(fileA) > File.GetCreationTime(fileB);
        }
        
        public IEnumerable<IFileMeta> GetFiles(string path, string mask, bool searchRecursively = false)
        {
            var info = new DirectoryInfo(path);
            return info.EnumerateFileSystemInfos(mask, searchRecursively ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly).Where(fsi => (fsi.Attributes & FileAttributes.Directory) != FileAttributes.Directory).Select(fi => new ReadonlyFileMeta(fi));
        }

        public Stream GetWriteableFile(params string[] path)
        {
            return File.Open(PathCombine(path), FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        }
        public Stream CreateWriteableFile(params string[] path)
        {
            return File.Open(PathCombine(path), FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        }

        public Stream GetReadableFile(params string[] path)
        {
            var file = PathCombine(path);

            return File.Open(file, FileMode.OpenOrCreate, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        }

        public IObservable<Unit> OnUpdate(string path, string pattern)
        {
            return Rxn.DfrCreate(() => Rxn.Create<Unit>(o =>
            {
                var watcher = Files.WatchForChanges(path, pattern, () => o.OnNext(new Unit()));

                //if we have an existing file, alert on it straight away
                //as the filewatcher doesnt until its changed
                //defer will allow the subscription to take effect so 
                //onnext will send the value to the subscriber. wihout it
                //the value will be sent to no one
                if(Directory.EnumerateFiles(path, pattern).Any())
                    o.OnNext(new Unit());

                return watcher;
            }));
        }

        public void Move(string source, string destination)
        {
            File.Move(source, destination);
        }

        public void SetFullAccessPermissions(string path, IEnumerable<string> usernames)
        {
        //{
        //    var permissions = new DirectorySecurity();

        //    foreach (var sid in usernames.Where(u => !String.IsNullOrWhiteSpace(u)))
        //        permissions.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));


        //    Directory.SetAccessControl(Environment.ExpandEnvironmentVariables(path), permissions);
        }

        public IFileMeta ToFileMeta(string filename, string contentType, DateTime lastWriteTime)
        {
            return new ReadonlyFileMeta()
            {
                Fullname = filename,
                ContentType = contentType,
                LastWriteTime = lastWriteTime
            };
        }
    }
}
