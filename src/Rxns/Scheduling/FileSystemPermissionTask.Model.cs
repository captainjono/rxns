using System.ComponentModel;
using System.Runtime.Serialization;

namespace Rxns.Scheduling
{
    [DataContract]
    public partial class FileSystemPermissionTask
    {
        [DataMember]
        [DefaultValue(default(string[]))]
        public string[] Usernames { get; set; }

        [DataMember]
        public FileSystemPermission Permission { get; set; }

        [DataMember]
        public string Path { get; set; }

    }
}
