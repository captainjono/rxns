using System;
using System.IO;
using Rxns.Interfaces;

namespace Janison.MicroApp
{
    public class ReadWriteFileMeta : IFileMeta
    {
        /// <summary>
        /// The name of the file
        /// </summary>
        public virtual string Name
        {
            get
            {
                return Fullname != null ? Fullname != "" ? Path.GetFileName(Fullname) : "" : null;
            }
        }

        /// <summary>
        /// The path of the file as recognized by the service that retrieves it
        /// null if this doesn't make sense for the use case
        /// </summary>
        public string Fullname { get; set; }
        /// <summary>
        /// The last time the file was written
        /// </summary>
        public DateTime LastWriteTime { get; set; }
        /// <summary>
        /// The hash of the file, if calculated. Use StreamExtensions.ComputeHash()
        /// to calculate this field from an existing stream
        /// </summary>
        public string Hash { get; set; }

        /// <summary>
        /// The content type in relation to http transfer of this
        /// object. This property is not always set, null value should
        /// indicate that this field has not been computed as yet, rather
        /// then this file not having a content type. Use string.ContentType() to derive value
        /// which is in Core.Http
        /// </summary>
        public string ContentType { get; set; }

        /// <summary>
        /// The length of the file
        /// </summary>
        public virtual long Length
        {
            get { return !String.IsNullOrWhiteSpace(Fullname) ? new FileInfo(Fullname).Length : 0; }
        }

        /// <summary>
        /// The readonly contents of the file
        /// </summary>
        public virtual Stream Contents
        {
            get { return !String.IsNullOrWhiteSpace(Fullname) ? File.Open(Fullname, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete) : null; }
            set { throw new NotImplementedException(); }
        }            /// <summary>

        /// <summary>
        /// Creates a new instance of the file meta object. Used when
        /// creating new files. 
        /// </summary>
        public ReadWriteFileMeta()
        {
            //ContentType = "application/octet-stream";
        }

        /// <summary>
        /// Creates a filemeta representation of a filesysteminfo object
        /// </summary>
        /// <param name="file">The info to parse</param>
        public ReadWriteFileMeta(FileSystemInfo file)
        {
            //Name = file.Name;
            Fullname = file.FullName;
            LastWriteTime = file.LastWriteTime;
        }


        /// <summary>
        /// Returns a string in the format
        /// {name}\t{lastwrite}\t{hash}
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return String.Format("{0}\t{1}\t{2}", Name, LastWriteTime.Ticks, Hash);
        }

    }   
}
