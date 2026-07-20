using System;
using System.Collections.Generic;
using System.IO;
using System.Reactive;

namespace Rxns.Interfaces
{
    public interface IFileSystemService
    {
        /// <summary>
        /// Combines paths to create a single qualified path
        /// 
        /// NOTE: leading \ for the 2nd param will because the first
        /// param to be ignored!!
        /// </summary>
        /// <param name="paths">A list of paths</param>
        /// <returns>The qualitified path</returns>
        string PathCombine(params string[] paths);

        /// <summary>
        /// The fastest way to copy two files on a windows machine
        /// 
        /// Also sets the created and last modified dates of the destination
        /// that of the source.
        /// </summary>
        /// <param name="source">The path to the source file</param>
        /// <param name="destination">The path to destination file</param>
        /// <param name="buffer">The size of buffer to use. 65536 is a good starting point due to frame alignment on disk</param>

        void Copy(string source, string destination, int buffer);

        void Move(string source, string destination);

        /// <summary>
        /// Determins whether two files are equal using a hashing algorithm
        /// on their file streams
        /// </summary>
        /// <param name="source">The source file</param>
        /// <param name="destination">The file to compare too</param>
        /// <returns>If they are equal or not</returns>
        bool AreEqual(string source, string destination);

        /// <summary>
        /// Deletes a file from the system
        /// </summary>
        /// <param name="destination">The file to delete</param>
        void DeleteFile(params string[] destination);

        /// <summary>
        /// Deletes a directory from the system, including all sub-directories
        /// </summary>
        /// <param name="destination"></param>
        void DeleteDirectory(params string[] destination);

        /// <summary>
        /// Determines whether a file exists or not
        /// </summary>
        /// <param name="file">The file to check</param>
        /// <returns>If it exists or not</returns>
        bool ExistsFile(string file);

        /// <summary>
        /// Determines whether a directory exists or not
        /// </summary>
        /// <param name="path">The directory to check</param>
        /// <returns>If it exists or not</returns>
        bool ExistsDirectory(string path);
        
        /// <summary>
        /// Returns true is fileA is newer then fileB
        /// based on the lastmodified and created date
        /// </summary>
        /// <param name="fileA">The file the to compare with the other file</param>
        /// <param name="fileB">The other file</param>
        /// <returns>If fileA is newer</returns>
        bool IsNewer(string fileA, string fileB);

        void CreateDirectory(string path);

        string GetFilePart(string pathToFile);

        string GetDirectoryPart(string pathToFile);

        IFileMeta GetOrCreateFile(string path);


        IEnumerable<IFileMeta> GetFiles(string path, string mask, bool searchREcursively = false);
        /// <summary>
        /// Monitors a particular directory and produces a value when a change is detected
        /// 
        /// The changes monitored are new or updated files, and it will procude a value on 
        /// inital subscription if the file exists.
        /// </summary>
        /// <param name="path">The path to watch</param>
        /// <param name="pattern">The pattern to watch for</param>
        /// <returns>A sequence of events when changes occour</returns>
        IObservable<Unit> OnUpdate(string path, string pattern); 

        /// <summary>
        /// Creates or appends to a existing file
        /// </summary>
        /// <param name="path">The file to create</param>
        /// <returns>A writeable stream</returns>
        Stream GetWriteableFile(params string[] path);
        
        Stream GetReadableFile(params string[] path);

        /// <summary>
        /// Creates a new file or overwrites an existing file
        /// </summary>
        /// <param name="path">The file to create</param>
        /// <returns>A writeable stream</returns>
        Stream CreateWriteableFile(params string[] path);

        void SetFullAccessPermissions(string path, IEnumerable<string> usernames);
        IFileMeta ToFileMeta(string filename, string contentType, DateTime lastWriteTime);
    }
}
