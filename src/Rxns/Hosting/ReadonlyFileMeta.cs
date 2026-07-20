using System;
using System.ComponentModel;
using System.IO;
using Rxns.Interfaces;

namespace Rxns
{
    /// <summary>
    /// The class holds meta information about a particular file, allowing the user to compare
    /// easily against other files for equality, changes etc..
    /// </summary>
    public class ReadonlyFileMeta : IFileMeta
    {
        /// <summary>
        /// The name of the file
        /// </summary>
        public virtual string Name
        {
            get
            {
                return Fullname != null ?  Fullname != "" ? Path.GetFileName(Fullname) : "" : null;
            }
        }

        /// <summary>
        /// The name of the file, in lower case, to support some weird requirement because of leap
        /// </summary>
        public string LegacyLowerCaseName
        {
            get { return Name != null ? Name.ToLower() : null; }
        }

        /// <summary>
        /// The path of the file as recognized by the service that retrieves it
        /// null if this doesn't make sense for the use case
        /// </summary>
        [DefaultValue(null)]
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

        public Stream Contents1
        {
            get { throw new NotImplementedException(); }
            set { throw new NotImplementedException(); }
        }

        /// <summary>
        /// The content type in relation to http transfer of this
        /// object. This property is not always set, null value should
        /// indicate that this field has not been computed as yet, rather
        /// then this file not having a content type. Use string.ContentType() to derive value
        /// which is in Core.Http
        /// </summary>
        [DefaultValue(null)]
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
            get { return !String.IsNullOrWhiteSpace(Fullname) ? File.Open(Fullname, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete) : null; }
            set { throw new NotImplementedException(); }
        }

        /// <summary>
        /// Creates a new instance of the file meta object. Used when
        /// creating new files. 
        /// </summary>
        public ReadonlyFileMeta()
        {
            //ContentType = "application/octet-stream";
        }

        /// <summary>
        /// Creates a filemeta representation of a filesysteminfo object
        /// </summary>
        /// <param name="file">The info to parse</param>
        public ReadonlyFileMeta(FileSystemInfo file)
        {
            //Name = file.Name;
            Fullname = file.FullName;
            LastWriteTime = file.LastWriteTime;
        }

        /// <summary>
        /// Parses filemeta from a string produced by the 
        /// .ToString() method
        /// </summary>
        /// <param name="fileMeta"></param>
        /// <returns></returns>
        public static ReadonlyFileMeta FromString(string fileMeta)
        {
            var meta = new ReadonlyFileMeta();
            var parts = fileMeta.Split('\t');

            //using if statements to support "broken" or corrupted indexes
            if (fileMeta.Length > 0)
                meta.Fullname = parts[0];

            if (fileMeta.Length > 1)
                meta.LastWriteTime = new DateTime(long.Parse(parts[1]));

            if (fileMeta.Length > 2)
                meta.Hash = parts[2];

            return meta;
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

        /// <summary>
        /// Used for determining equality for lists, dictionarys
        /// others...
        /// </summary>
        /// <returns>whether name and lastwritetime are equal</returns>
        public override bool Equals(object obj)
        {
            if (obj is ReadonlyFileMeta)
                return (((ReadonlyFileMeta)obj).Name == Name)
                    && (((ReadonlyFileMeta)obj).LastWriteTime == LastWriteTime);

            return base.Equals(obj);
        }

        /// <summary>
        /// Used for determining equality for lists, dictionarys
        /// others...
        /// </summary>
        /// <returns>The hash code of the object</returns>
        public override int GetHashCode()
        {
            return (Name + LastWriteTime).GetHashCode();
        }
    }
}
