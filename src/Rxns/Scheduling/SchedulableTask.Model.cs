using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.Serialization;

namespace Rxns.Scheduling
{
    /// <summary>
    /// This class is used by parsers to format the serialized version
    /// of a the ScheduableTask class.
    /// Default values are used to reduce the serialized size of the class, they
    /// allow the properties to be omitted when they are set to this value.
    /// </summary>
    [DataContract]
    public partial class SchedulableTask
    {
        /// <summary>
        /// The name used by the JSON configuration to determine which
        /// class to instantiate. So when referencing a specific plugin type,
        /// this translates to the class name, without any namespace prefixes.
        /// </summary>
        [DataMember]
        public string PluginName
        {
            get { return GetType().Name; }
        }

        [DataMember]
        [DefaultValue(true)]
        public bool IsSynchronous { get; set; }

        [DataMember]
        [DefaultValue(true)]
        public bool IsBlocking { get; set; }
        
        [DataMember]
        public List<ExecutionCondition> Conditions { get; set; }

        [DataMember]
        public List<InputParameter> InputParameters { get; set; }

        [DataMember]
        public List<OutputParameter> OutputParameters { get; set; }

        #region Newton JSON serialization methods

        /// <summary>
        /// Determines whether the Conditions list should be serialized or not
        /// </summary>
        /// <returns>As comment</returns>
        public bool ShouldSerializeConditions()
        {
            return Conditions.Count != 0;
        }

        /// <summary>
        /// Determines whether the InputParameters list should be serialized or not
        /// </summary>
        /// <returns>As comment</returns>
        public bool ShouldSerializeInputParameters()
        {
            return InputParameters.Count != 0;
        }

        /// <summary>
        /// Determines whether the OutputParameters list should be serialized or not
        /// </summary>
        /// <returns>As comment</returns>
        public bool ShouldSerializeOutputParameters()
        {
            return OutputParameters.Count != 0;
        }

        #endregion
    }
}
