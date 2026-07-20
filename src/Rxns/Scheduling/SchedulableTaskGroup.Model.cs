using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.Serialization;

namespace Rxns.Scheduling
{
    [DataContract]
    public partial class SchedulableTaskGroup
    {
        public int ID { get; set; }

        [DataMember]
        public string Name { get; set; }

        [DataMember]
        [DefaultValue(null)]
        public TimeSpan? TimeSpanSchedule { get; set; }

        [DataMember]
        [DefaultValue(false)]
        public bool IsEnabled { get; set; }

        [DataMember]
        public List<ISchedulableTask> Tasks { get; set; }
        
        [DataMember]
        public List<OutputParameter> Parameters { get; set; }

        /// <summary>
        /// if true, the system will send a systemstatusmeta event when it 
        /// is run the first time which will report on the groups execution state
        /// </summary>
        [DataMember]
        [DefaultValue(false)]
        public bool IsReporting { get; set; }

        public bool ShouldSerializeParameters()
        {
            return Parameters.Count != 0;
        }
        public bool ShouldSerializeTasks()
        {
            return Tasks.Count != 0;
        }
    }
}
