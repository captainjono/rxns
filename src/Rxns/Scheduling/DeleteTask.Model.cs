using System.ComponentModel;
using System.Runtime.Serialization;

namespace Rxns.Scheduling
{
    [DataContract]
    public partial class DeleteTask
    {
        /// <summary>
        /// The files to delete
        /// </summary>
        [DataMember]
        [DefaultValue(default(string[]))]
        public string[] Files { get; set; }

        /// <summary>
        /// The folder to delete
        /// </summary>
        [DataMember]
        [DefaultValue(default(string[]))]
        public string[] Directories { get; set; }
    }
}
