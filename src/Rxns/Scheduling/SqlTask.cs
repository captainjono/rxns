using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Rxns.Collections;
using Rxns.Interfaces;

namespace Rxns.Scheduling
{
    /// <summary>
    /// Use this task to run stored prodecures in a resilliant way.
    /// 
    /// The tasks implements various features which makes the outcome of sql more predictable
    /// - support for intrinsic transactions for the sql to execute. Seperates transactions based on go statements. UseTransactions to toggle
    /// - ignores common or all sql errors if u just want scripts to run blindly
    /// - logs if tables are creates and deletes them if the task then fails. ie implicit transactions for create table statements
    /// - generates drop and statements for stored procs and functions, so they can be repeatidly applied without error
    /// - can take a set of create table scripts given in any order, and install them in the proper order demanded by any table-relationships
    /// </summary>
    public partial class SqlTask : SchedulableTask, ITask
    {
        private readonly IDatabaseConnection _database;
        private readonly IDefaultDbCfg _cfg;
        private readonly IFileSystemService _fileSystem;
        private readonly IZipService _zipService;

        /// <summary>
        /// The state used for substituions in the scripts before they are executed.
        /// This can be set alternatively via the executeasync method
        /// </summary>
        public ExecutionState State { get; set; }
        /// <summary>
        /// the expression used to identify if a script is a create table script for the purposes of tracking database creation for rolling back
        /// </summary>
        public static Regex CreateOrAlterDatabaseExp = new Regex(@"\s*\b(ALTER|CREATE|DROP|BACKUP) DATABASE\b", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        public static Regex CreateFunctionExp = new Regex(@"\s*\b(ALTER|CREATE) FUNCTION\b", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        public static Regex CreateStoredProcExp = new Regex(@"\s*\b(ALTER|CREATE) PROCEDURE\b", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        public static Regex CreateTriggerExp = new Regex(@"\s*\b(ALTER|CREATE) TRIGGER\b", RegexOptions.IgnoreCase | RegexOptions.Multiline);

        public override string ReporterName
        {
            get { return String.Format("SqlTask<{0}>", Path); }
        }

        public SqlTask(IDatabaseConnection database, IDefaultDbCfg cfg, IFileSystemService fileSystem, IZipService zipService)
        {
            _zipService = zipService;
            _fileSystem = fileSystem;
            _database = database;
            _cfg = cfg;
            IgnoreCreateErrors = true;
            UseTransactions = true;
        }

        /// <summary>
        /// Executes the scripts provided against the database specified
        /// - Use GO statements to segment scripts into transactions
        /// - Databases created by this task will be tracked, and if the overall execution fails,
        /// they will be dropped.
        /// - create table REFERENCES are respected such that the task will
        /// re-order the script execution (otherwise defaults to alphabetical order) such that 
        /// that reference tables are created first. 
        /// </summary>
        /// <param name="scripts">The scripts to run</param>
        /// <returns>When the task finishes execution</returns>
        public IObservable<Unit> Execute(IEnumerable<string> scripts, string connectionString = null, string database = null)
        {
            connectionString = connectionString ?? ConnectionString ?? _cfg.ConnectionString;
            database = database ?? Database;

            if (!scripts.Any()) //nothing to do
                return new Unit().ToObservable();

            return Observable.Create<Unit>(o =>
            {
                var createdDatabases = new List<string>();
                var count = 0;

                foreach (var script in scripts.SelectMany(s => s.SplitOnGos().GenerateDropsForProcsAndFunctions()).SortByDependencies(new Func<string, string>[] { GetIdFromCreateTable }, new Func<string, string[]>[] { GetCreateTableDependencies }).ToArray())
                {
                    count++;
                    try
                    {
                        if (script.IsCreateOrAlterDatabase() || !UseTransactions) //cant run these in an transaction, so we simulate a transaction with dropdatabases below
                        {
                            _database.ExecuteScript(connectionString, script, database).Wait();

                            //the "transaction"
                            if (script.Contains("create database", StringComparison.OrdinalIgnoreCase))
                            {
                                var dbName = GetDatabaseNameFromCreateOrAlter(script);
                                if (dbName != null)
                                    createdDatabases.Add(dbName);
                            }
                        }
                        else
                        {
                            ExecuteWithTransaction(script, connectionString, database);
                        }

                    }
                    catch (RxSqlException e)
                    {
                        if (IgnoreAllErrors) continue;

                        if (IgnoreCreateErrors)
                            if (e.Number == 2714 //There is already an object named 'abc' in the database.
                                || e.Number == 1801 //Database 'abc' already exists. Choose a different database name.
                                || e.Number == 15025 //The server principal 'abc' already exists.
                                || e.Number == 15023 //User, group, or role 'dbo' already exists in the current database.
                                || e.ToString().Contains("already has", StringComparison.OrdinalIgnoreCase) //Column already has a default bound to it
                                || e.ToString().Contains("already exists", StringComparison.OrdinalIgnoreCase) //catch all
                                || e.ToString().Contains("already enabled", StringComparison.OrdinalIgnoreCase)) //changetracking is already enabled
                                continue;

                        DropDatabases(createdDatabases);
                        OnWarning("While executing: [Length:{1}] {0}", script.ToStringMax(300), script.Length);
                        Result.SetAsFailure(e);
                        o.OnError(e);
                    }
                    catch (Exception e)
                    {
                        DropDatabases(createdDatabases);
                        Result.SetAsFailure(e);
                        o.OnError(e);
                    }
                }

                Result.SetAsSuccess(count);

                o.OnNext(new Unit());
                o.OnCompleted();

                return Disposable.Empty;
            });

        }

        private void ExecuteWithTransaction(string script, string connectionString, string database)
        {
            using (var transaction = _database.BeginTransaction(connectionString, database))
            {
                _database.ExecuteScript(script, transaction).Wait();
                transaction.Commit();
            }
        }

        public virtual IObservable<Unit> Execute()
        {
            var scripts = GetScriptsAndPrepare(Path);

            return Execute(scripts, ConnectionString, Database);
        }

        private string GetDatabaseNameFromCreateOrAlter(string script)
        {
            var res = CreateOrAlterDatabaseExp.Split(script);
            if (res.Length < 3)
                return null;

            return CreateOrAlterDatabaseExp.Split(script)[2].Trim();
        }

        private void DropDatabases(IEnumerable<string> createdDatabases)
        {
            foreach (var database in createdDatabases)
                try
                {
                    _database.ExecuteScript(ConnectionString, String.Format("DROP DATABASE {0}", database));
                }
                catch (Exception e)
                {
                    OnWarning("While winding back created databases '{0}', database may be in an inconsistant state\r\n{1}", createdDatabases.ToStringEach(), e);
                }
        }

        public override Task<ExecutionState> ExecuteTaskAsync(ExecutionState state)
        {
            return Execute().Select(_ => state).ToTask();
        }

        protected override void BindToState(ExecutionState state)
        {
            State = state;//used by the executetask
            ConnectionString = String.IsNullOrWhiteSpace(ConnectionString) ? BindToState("{ConnectionString}", state, _cfg.ConnectionString).ToString() :
                                                                             BindToState(ConnectionString, state).ToString();

            Path = String.IsNullOrWhiteSpace(Path) ? BindToState("{Path}", state).ToString() :
                                                     BindToState(Path, state).ToString();
            Database = String.IsNullOrWhiteSpace(Database) ? BindToState("{Database}", state, String.Empty).ToString() :
                                                             BindToState(Database, state).ToString();

            //this applies a binding to the connectinstring with values from the cfg class
            PerformConnectionStringToTypeBinding(_cfg);

            base.BindToState(state);
        }

        private void PerformConnectionStringToTypeBinding<T>(T typeWithPropertie) where T : class
        {
            if (!ConnectionString.IsBinding() || typeWithPropertie == null) return;

            var binding = typeWithPropertie.GetType()
                .GetProperties()
                .FirstOrDefault(m => ConnectionString.Trim('{', '}').Equals(m.Name, StringComparison.OrdinalIgnoreCase));

            if (binding != null)
                ConnectionString = binding.GetValue(typeWithPropertie, null).ToString();
        }

        /// <summary>
        /// This method takes a path to either a sql file or a zip file containing sql files
        /// then extracts all the scripts embedded in these files. After finding the scripts
        /// it applys bindings based on the [binding] syntax and the current state.
        /// After applying bindings it then generates drop statements for the 
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        protected IEnumerable<string> GetScriptsAndPrepare(string path)
        {
            IEnumerable<IFileMeta> resource = null;

            try
            {
                IEnumerable<StreamReader> files = null;

                //do we have a file to search?
                if (!String.IsNullOrWhiteSpace(path) && _fileSystem.ExistsFile(path))
                {
                    //if we have a zip, read the contents
                    if (!path.EndsWith("zip", StringComparison.OrdinalIgnoreCase))
                        files = new[] { new StreamReader(_fileSystem.GetReadableFile(path)) };
                    else
                    {
                        resource = _zipService.GetFiles(path, ".sql").ToEnumerable().ToArray();
                        files = resource.Select(s => new StreamReader(s.Contents));
                    }
                }
                else //we have a directory, search it
                {
                    files = _fileSystem.GetFiles(path, "*.sql", true).Select(s => new StreamReader(_fileSystem.GetReadableFile(s.Fullname)));
                }

                return files.Select(script => { using (script) return script.ReadToEnd(); }).ToArray().ApplyBindings(State);
            }
            finally
            {
            }
        }

        private string GetIdFromCreateTable(string script)
        {
            var createSyntax = "CREATE TABLE ";

            return !script.Contains(createSyntax, StringComparison.OrdinalIgnoreCase) ?
                null :
                script.ToUpper().GetSqlObjectName(createSyntax).RemoveSqlQualification();
        }

        private string[] GetCreateTableDependencies(string script)
        {
            var tokens = script.ToUpper().Split(new[] { "REFERENCES " }, StringSplitOptions.RemoveEmptyEntries);

            return tokens.Length == 1 ?
                new string[] { } :
                tokens.Skip(1).Select(entry => entry.Split(' ')[0].RemoveSqlQualification()).ToArray();
        }
    }

    
        internal class ScriptDependency
        {
            public string Id { get; set; }
            public string Script { get; set; }
            public int State;
            public IEnumerable<string> DependsOn { get; set; }

            public override string ToString()
            {
                return Id;
            }
        }

        internal static class SqlTaskExtensions
        {
            internal static bool IsCreateOrAlterDatabase(this string script)
            {
                return SqlTask.CreateOrAlterDatabaseExp.IsMatch(script);
            }

            internal static IEnumerable<string> ApplyBindings(this IEnumerable<string> scripts, ExecutionState state)
            {
                if (state == null || !state.Variables.Any())
                    return scripts;

                return scripts.Select(s => state.Variables.Aggregate(s, (current, binding) =>
                {
                    var bindingValue = binding.Value == null ? "" : binding.Value.ToString();
                    return current.Replace(String.Format("[{0}]", binding.Key), bindingValue).Replace(String.Format("'{0}'", binding.Key.Trim('[', ']')), String.Format("'{0}'", bindingValue));
                }));
            }

            internal static IEnumerable<string> GenerateDropsForProcsAndFunctions(this IEnumerable<string> scripts)
            {
                var mappings = new Dictionary<Regex, Tuple<string, string>>();
                mappings.Add(SqlTask.CreateStoredProcExp, new Tuple<string, string>("PROCEDURE", "N'P', N'PC'"));
                mappings.Add(SqlTask.CreateFunctionExp, new Tuple<string, string>("FUNCTION", "N'FN', N'IF', N'TF', N'FS', N'FT'"));
                mappings.Add(SqlTask.CreateTriggerExp, new Tuple<string, string>("TRIGGER", "N'TR'"));

                var dropStatementFormat = @"IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'{0}') AND type in ({2}))
                                          DROP {1} {0}";

                return scripts.SelectMany(script =>
                {
                    foreach (var mapping in mappings)
                    {
                        if (!mapping.Key.IsMatch(script)) continue;
                        var objectName = script.GetSqlObjectName(mapping.Key);
                        var dropStatement = String.Format(dropStatementFormat, objectName, mapping.Value.Item1, mapping.Value.Item2);

                        return new[] { dropStatement, script };
                    }

                    return new[] { script };
                });
            }

            internal static string GetSqlObjectName(this string script, string startFragment)
            {
                if (!script.Contains(startFragment)) return null;

                var tokens = script.Split(new[] { startFragment }, StringSplitOptions.RemoveEmptyEntries);
                var index = tokens.Length > 1 ? 1 : 0;

                return tokens[index];
            }

            internal static string GetSqlObjectName(this string script, Regex startFragment)
            {
                var tokens = startFragment.Split(script);

                var index = tokens.Length > 2 ? 2 : 0;

                return tokens[index].TrimStart().Split(' ', '(', '\n')[0].Trim();
            }


            /// <summary>
            /// Splits up a SQL script at GO boundaries
            /// GO statements must be the first and/or last tokens on a line
            /// otherwise the GO is considerd to be part of the current script
            /// boundary. As such, this is a subset of the tokens accepted and processed
            /// by SQL MGNT studio.
            /// </summary>
            /// <param name="script"></param>
            /// <returns></returns>
            internal static IEnumerable<string> SplitOnGos(this string script)
            {
                var statements = Regex
                        .Split(script, @"^\s*\bGO\b\s* ($ | \-\- .*$)", RegexOptions.Multiline | RegexOptions.IgnorePatternWhitespace | RegexOptions.IgnoreCase) // Split by "GO" statements
                        .Where(x => !String.IsNullOrWhiteSpace(x) && !x.Equals("go", StringComparison.OrdinalIgnoreCase)); // Remove empties, trim, and return

                return statements;
            }

            /// <summary>
            /// This function parses scripts and determines if they have any dependencies on other scripts, based on the functions provided.
            /// The result is an ordered list that can be executed against sql server without worrying about dependency errors.
            /// </summary>
            /// <param name="scripts">The scripts to examin for dependencies</param>
            /// <param name="getIds">Used to determin the name of the dependency for the current script. If the function cannot parse a name, null should be returned.</param>
            /// <param name="getDependencies">Determines the names of dependencies by examining the specification the script. Such dependencies mean that script A must be executed before script B</param>
            /// <returns>An ordered list based on the dependency relationships</returns>
            internal static IEnumerable<string> SortByDependencies(this IEnumerable<string> scripts, IEnumerable<Func<string, string>> getIds, IEnumerable<Func<string, string[]>> getDependencies)
            {
                return scripts
                    .Select(s =>
                    {
                        //find a name
                        var id = getIds.Select(getId => getId(s)).FirstOrDefault(name => name != null && !name.StartsWith("#")); //# = temp table

                        //create a dependency object and initalise it
                        return new ScriptDependency()
                        {
                            Id = id ?? Guid.NewGuid().ToString(),
                            Script = s,
                            State = ALIVE,
                            DependsOn = s.FindDependencies(getDependencies).ToArray()
                        };
                    })
                    .ToArray()
                    .Sort();
            }

            internal static IEnumerable<string> FindDependencies(this string sqlScript, IEnumerable<Func<string, string[]>> getDependencies)
            {
                return getDependencies.SelectMany(getDependency => getDependency(sqlScript));
            }

            /// <summary>
            /// Topological sort implementation that ignores circular dependencies
            /// </summary>
            /// <param name="unsorted">The unsorted dependency tree</param>
            /// <returns>A list of scripts that can be executed without dependency conflicts</returns>
            internal static IEnumerable<string> Sort(this ScriptDependency[] unsorted)
            {
                var state = new Dictionary<string, int>();
                var sorted = new List<ScriptDependency>();

                //veryify the script id is unique before adding it
                foreach (var item in unsorted)
                    if (!state.ContainsKey(item.Id))
                        state.Add(item.Id, ALIVE);
                    else
                        throw new Exception(String.Format("Two or more sql objects found with ids '{0}' discovered.\r\n {1}", item.Id, item.Script.ToStringMax(50)));

                //now search for dependencies
                foreach (var script in unsorted)
                    SortVisitImpl(unsorted, script, ref sorted, ref state);

                return sorted.Select(s => s.Script);
            }

            private const int ALIVE = 0;
            private const int DEAD = 1;
            private const int UNDEAD = 2;

            /// <summary>
            /// A recursive, depth first search
            /// </summary>
            /// <param name="original">The original </param>
            /// <param name="current"></param>
            /// <param name="sorted"></param>
            /// <param name="state"></param>
            private static void SortVisitImpl(ScriptDependency[] original, ScriptDependency current, ref List<ScriptDependency> sorted, ref Dictionary<string, int> state)
            {
                //if we cant find the dependency (null, its not created in our script) or ...
                if (current == null || state[current.Id] == DEAD) return;
                if (state[current.Id] == UNDEAD) return;

                state[current.Id] = UNDEAD;

                if (current.DependsOn.AnyItems())
                    foreach (var neighbour in current.DependsOn)
                        SortVisitImpl(original, original.FirstOrDefault(o => o.Id == neighbour), ref sorted, ref state);

                state[current.Id] = DEAD;

                sorted.Add(current);
            }
        }
    
        public static class SqlTaskEx 
        {
            /// <summary>
            /// A basic method to extract table/database names from [dboQualified].[sql]
            /// 
            /// note: returns a toLowered() string
            /// </summary>
            /// <param name="sqlString"></param>
            /// <returns></returns>
            public static string RemoveSqlQualification(this string sqlString)
            {
                return sqlString.ToLower().Replace("[dbo].", "").Split(' ')[0].Trim().Split(']')[0].Trim('[', ']', ')', '(', ' ');
            }
    }
}
