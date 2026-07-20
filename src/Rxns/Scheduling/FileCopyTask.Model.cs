using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.Serialization;

namespace Rxns.Scheduling
{
    [DataContract]
    public partial class FileCopyTask
    {
        /// <summary>
        /// The list of files to copy
        /// </summary>
        [DataMember]
        public List<string> Files { get; set; }

        /// <summary>
        /// The source directory where the files exist
        /// </summary>
        [DataMember]
        [DefaultValue(null)]
        public string Source { get; set; }

        /// <summary>
        /// The destination folder that the files will be copied too
        /// </summary>
        [DataMember]
        [DefaultValue(null)]
        public string Destination { get; set; }

        /// <summary>
        /// When set, the source directory is taken as the root dir.
        /// If a file in the file list contains dir1/file.txt, this flag
        /// will force dir1 to be created the file to be copied into it in the
        /// destination. if not, the file will just be copied to the destination
        /// </summary>
        [DataMember]
        [DefaultValue(false)]
        public bool PreserveDirectoryStructure { get; set; }

        /// <summary>
        /// the size of the file read/write buffer
        /// </summary>
        [DataMember]
        [DefaultValue(0)]
        public int Buffer { get; set; } = 1024;

        /// <summary>
        /// the number of simultaneous copies allowed
        /// </summary>
        [DataMember]
        [DefaultValue(0)]
        public int Threads { get; set; } = 1;

        /// <summary>
        /// Whether of not the system will validate the new copy for the file
        /// Failing validation, the system will delete the corrupted file
        /// in the destination directory
        /// </summary>
        [DataMember]
        [DefaultValue(false)]
        public bool ValidateCopy { get; set; }

        /// <summary>
        /// The number of times the system will try and copy a file
        /// before failing
        /// </summary>
        [DataMember]
        [DefaultValue(0)]
        public int RetryCount { get; set; }

        /// <summary>
        /// If the task will throw and error or a warning if a copy
        /// that is to be copied could not be found
        /// </summary>
        [DataMember]
        [DefaultValue(false)]
        public bool ErrorOnNotFound { get; set; }

        [DataMember]
        [DefaultValue(null)]
        public Dictionary<string, string> FileMap { get; set; } 

        public bool ShouldSerializeFiles()
        {
            return Files.Count != 0;
        }
    }
}
