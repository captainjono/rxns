using System.ComponentModel;
using System.Runtime.Serialization;

namespace Rxns.Scheduling
{
    [DataContract]
    public partial class SqlTask
    {
        /// <summary>
        /// The path to the file or directory that will be executed
        /// by the task
        /// </summary>
        [DataMember]
        [DefaultValue(null)]
        public string Path { get; set; }

        /// <summary>
        /// Tells the task to not fail with an error
        /// if a create statement fails because an object of the
        /// same name already exists
        /// </summary>[DataMember]
        [DefaultValue(true)]
        public bool IgnoreCreateErrors { get; set; }

        /// <summary>
        /// Ignores all errors no matter what when executing scripts.
        /// </summary>
        [DataMember]
        [DefaultValue(false)]
        public bool IgnoreAllErrors { get; set; }
        [DataMember]
        [DefaultValue(null)]
        public string ConnectionString { get; set; }
        [DataMember]
        [DefaultValue(true)]
        public bool UseTransactions { get; set; }
        /// <summary>
        /// overrides the default database as specified by the connection string
        /// </summary>
        [DataMember]
        [DefaultValue(null)]
        public string Database { get; set; }
    }
}
