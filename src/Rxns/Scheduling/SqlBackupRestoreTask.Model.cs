using System.ComponentModel;
using System.Runtime.Serialization;

namespace Rxns.Scheduling
{

    [DataContract]
    public partial class SqlBackupRestoreTask
    {
        /// <summary>
        /// The database to backup
        /// </summary>
        [DataMember]
        [DefaultValue(null)]
        public string Database { get; set; }
        /// <summary>
        /// The location to store the backup
        /// </summary>
        [DataMember]
        [DefaultValue("backups")]
        public string Path { get; set; }
        /// <summary>
        /// The connectionstring used to connect the database server. Note:
        /// for restore operations, the context of the connection must be different
        /// from the database being restored. If it is, the connection context will
        /// be switched to tempdb
        /// </summary>
        [DataMember]
        [DefaultValue(null)]
        public string ConnectionString { get; set; }
        /// <summary>
        /// The to perform; Default is backup
        /// </summary>
        [DataMember]
        [DefaultValue(SqlOperation.Backup)]
        public SqlOperation Operation { get; set; }

        /// <summary>
        /// Controls whether or not the task will fail if a backup/restore can be completed
        /// </summary>
        [DataMember]
        public bool FailOnError { get; set; }
    }

    public enum SqlOperation
    {
        Backup,
        Restore
    }
}
