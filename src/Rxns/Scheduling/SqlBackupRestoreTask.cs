using System;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading.Tasks;
using Rxns.Hosting;
using Rxns.Interfaces;

namespace Rxns.Scheduling
{
    public partial class SqlBackupRestoreTask : SchedulableTask, ITask
    {
        /// <summary>
        /// The name of the service used to define the  sql server system process
        /// </summary>
        public static string ServiceName = "sql server";

        public override string ReporterName
        {
            get { return String.Format("SQL{0}<{1}>", Enum.GetName(typeof (SqlOperation), Operation), Database); }
        }

        private const string _backupScriptFormat = @"backup database {0} TO disk='{1}'";
        private const string _restoreScriptFormat = @"
                                                    DECLARE UserCursor CURSOR LOCAL FAST_FORWARD FOR
                                                    SELECT
                                                        spid
                                                    FROM
                                                        master.dbo.sysprocesses
                                                    WHERE DB_NAME(dbid) = '{0}' --the database to look for connections too
                                                    AND SPID > 50 -- To not try to Kill SQL Server Process
                                                    AND SPID != @@SPID -- To not kill yourself
                                                    DECLARE @spid SMALLINT
                                                    DECLARE @SQLCommand VARCHAR(300)
                                                    OPEN UserCursor
                                                    FETCH NEXT FROM UserCursor INTO
                                                        @spid
                                                    WHILE @@FETCH_STATUS = 0
                                                    BEGIN
                                                        SET @SQLCommand = 'KILL ' + CAST(@spid AS VARCHAR)
                                                        EXECUTE(@SQLCommand)
                                                        FETCH NEXT FROM UserCursor INTO
                                                            @spid
                                                    END
                                                    CLOSE UserCursor
                                                    DEALLOCATE UserCursor
                                                    
                                                    ALTER DATABASE [{0}]
                                                    SET SINGLE_USER WITH
                                                    ROLLBACK AFTER 60 --this will give your current connections 60 seconds to complete
                        
                                                    --Do Actual Restore
                                                    RESTORE DATABASE {0}
                                                    FROM DISK = '{1}'
                                                    WITH REPLACE

                                                    ALTER DATABASE [{0}]
                                                    SET MULTI_USER";

        private IDatabaseConnection _database;
        private IDefaultDbCfg _configuration;
        private IOperationSystemServices _osServices;
        private IFileSystemService _fileSystem;

        public SqlBackupRestoreTask(IDatabaseConnection database, IDefaultDbCfg cfg, IFileSystemService fileSystem, IOperationSystemServices osServices)
        {
            _osServices = osServices;
            _database = database;
            _fileSystem = fileSystem;
            _configuration = cfg;
        }

        public IObservable<Unit> Execute()
        {
            if (System.StringExtensions.IsNullOrWhitespace(Database)) throw new ArgumentException("Database must be set before executing", "Database");

            Path = Path ?? String.Format(@"backups\{0}.bak", Database.RemoveSqlQualification());

            EnsurePathAccessibleBySqlServer(Path);

            var task = _database.ExecuteScript(ConnectionString ?? _configuration.ConnectionString, String.Format(Operation == SqlOperation.Backup ? _backupScriptFormat : _restoreScriptFormat, Database, System.IO.Path.GetDirectoryName(Path), "tempdb"));

            if (!FailOnError) task = task.OnErrorResumeNext(new Unit().ToObservable());

            return task;
        }

        private void EnsurePathAccessibleBySqlServer(string path)
        {
            var users = _osServices.GetServiceUsers(ServiceName);

            //create dir
            var backupDir = System.IO.Path.GetDirectoryName(path);
            _fileSystem.CreateDirectory(backupDir);

            //set permissions
            _fileSystem.SetFullAccessPermissions(backupDir, users);
        }

        public override Task<ExecutionState> ExecuteTaskAsync(ExecutionState state)
        {
            return Execute().Select(_ => state).ToTask();
        }

        protected override void BindToState(ExecutionState state)
        {
            ConnectionString = String.IsNullOrWhiteSpace(ConnectionString) ? BindToState("{ConnectionString}", state, _configuration.ConnectionString).ToString() :
                                                                             BindToState(ConnectionString, state).ToString();

            Path = String.IsNullOrWhiteSpace(Path) ? BindToState("{Path}", state).ToString() :
                                                     BindToState(Path, state).ToString();

            Database = String.IsNullOrWhiteSpace(Database) ? BindToState("{Database}", state, String.Empty).ToString() :
                                                             BindToState(Database, state).ToString();

            //this applies a binding to the connectinstring with values from the cfg class
            PerformConnectionStringToRvConfigurationBinding();

            base.BindToState(state);
        }

        private void PerformConnectionStringToRvConfigurationBinding()
        {
            if (!ConnectionString.IsBinding() || _configuration == null) return;

            var binding = _configuration.GetType()
                .GetProperties()
                .FirstOrDefault(m => ConnectionString.Trim('{', '}').Equals(m.Name, StringComparison.OrdinalIgnoreCase));

            if (binding != null)
                ConnectionString = binding.GetValue(_configuration, null).ToString();
        }
    }

}
