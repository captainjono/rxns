using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.Serialization;

namespace Rxns.Scheduling
{
    [DataContract]
    public partial class ExecuteGroupTask
    {        
        /// <summary>
        /// The groups that are required to have finished executing before the group is run
        /// </summary>
        [DataMember]
        public List<string> GroupsToWaitOn { get; set; }
        /// <summary>
        /// The target group to execute
        /// </summary>
        [DataMember]
        [DefaultValue(null)]
        public string Group { get; set; }
        /// <summary>
        /// How long the task will block and/or wait for
        /// </summary>
        [DataMember]
        [DefaultValue(WaitOptions.ParentGroup)]
        public WaitOptions Options { get; set; }

        public bool ShouldSerializeGroupsToWaitOn()
        {
            return GroupsToWaitOn.Count != 0;
        }
    }
}
