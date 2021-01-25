using System;
using System.ComponentModel;
using System.Runtime.Serialization;

namespace Rxns.Scheduling
{
    [DataContract]
    public partial class StoredProcTask
    {
        [DataMember]
        [DefaultValue(null)]
        public string ConnectionString { get; set; }
        [DataMember]
        [DefaultValue(null)]
        public string DefaultSqlSchema { get; set; }
        [DataMember]
        [DefaultValue(null)]
        public string StoredProcedureName { get; set; }
        [DataMember]
        [DefaultValue(null)]
        public string Database { get; set; }
        
        public string QualifiedStoredProcedureName
        {
            get { return String.Format("[{0}].[{1}]", DefaultSqlSchema ?? "dbo", StoredProcedureName); }
        }
    }
}
