using System.ComponentModel;
using System.Runtime.Serialization;

namespace Rxns.Scheduling
{

    [DataContract]
    public partial class CmdTask
    {
        [DataMember]
        [DefaultValue(null)]
        public string Command { get; set; }
    }
}
