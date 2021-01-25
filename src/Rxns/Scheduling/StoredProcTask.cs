using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Threading.Tasks;
using System.Threading.Tasks;

namespace Rxns.Scheduling
{
    /// <summary>
    /// This class implements a stored procedure task. The task takes a stored procedure name
    /// and optional input and output parameters that map to variables that the stored procedure
    /// can expect.
    /// </summary>
    public partial class StoredProcTask : SchedulableTask
    {
        protected readonly IDatabaseConnection _connection;

        public override string ReporterName { get { return String.Format("SP<{0}>", StoredProcedureName); } }

        /// <summary>
        /// creates a new instance of the class, setting default values based on the provided
        /// parameters. 
        /// </summary>
        /// <param name="defaultConfiguration">The default configuration for the task</param>
        /// <param name="connection">The connection to use to execute the stored procedure</param>
        public StoredProcTask(IDatabaseConnection connection)
        {
            _connection = connection;
        }

        /// <summary>
        /// Executes the stored procedure. Uses the provided state for binding to 
        /// input and output paremeters. 
        /// The RunningResult is set to the rows effected by the stored procedures
        /// execution at the completion of the task
        /// </summary>
        /// <param name="state">The state used for parameter binding</param>
        /// <returns>An updated state based on the results of the stored procedure</returns>
        public override Task<ExecutionState> ExecuteTaskAsync(ExecutionState state)
        {
            var qualifiedStoredProcedureName = String.Format("[{0}].[{1}]", DefaultSqlSchema, StoredProcedureName);

            OnVerbose("Executing SP: '{0}' with params {1}", qualifiedStoredProcedureName, InputParameters.PrettyPrint(OutputParameters));

            var task = _connection.ExecuteStoredProcedure(ConnectionString, qualifiedStoredProcedureName, InputParameters, OutputParameters, Database);

            return new AnonymousObservable<ExecutionState>(o =>
            {
                return task.Subscribe(outputV =>
                {
                    var output = outputV.ToList();

                    //publish any output variables to the state that may have changed as a result of the SP call
                    o.OnNext(CompleteExecution(state, output));
                    o.OnCompleted();
                },
                error =>
                {
                    o.OnError(error);
                });
            }).ToTask();
        }

        protected ExecutionState CompleteExecution(ExecutionState state, IEnumerable<OutputParameter> output)
        {
            Result.Name = StoredProcedureName;
            Result.Value = String.Format("{0}", output.FirstOrDefault(v => v.Name.Equals(String.Format("__return_{0}", StoredProcedureName), StringComparison.CurrentCultureIgnoreCase)));

            OnVerbose("Finished executing SP '{0}' results {1}", StoredProcedureName, output.PrettyPrint());

            return UpdateOutputParameter(output, state);
        }

        /// <summary>
        /// Binds the following variables to the state, if they are null or empty:
        /// ConnectionString, DefaultSqlSchem, StoredProcedureName
        /// </summary>
        /// <param name="state">The state to use for binding</param>
        protected override void BindToState(ExecutionState state)
        {
            ConnectionString = String.IsNullOrEmpty(ConnectionString) ? BindToState("{ConnectionString}", state).ToString() :
                                                                        BindToState(ConnectionString, state).ToString();

            DefaultSqlSchema = String.IsNullOrEmpty(DefaultSqlSchema) ? BindToState("{DefaultSqlSchema}", state, "dbo").ToString() :
                                                                        BindToState(DefaultSqlSchema, state).ToString();

            StoredProcedureName = String.IsNullOrEmpty(StoredProcedureName) ? BindToState("{StoredProcedureName}", state).ToString() :
                                                                              BindToState(StoredProcedureName, state).ToString();

            try
            {
                if (String.IsNullOrEmpty(Database)) Database = BindToState("{Database}", state).ToString();
            }
            catch (Exception)
            {
                //doesnt matter null is valid
            }

            base.BindToState(state);
        }
    }

    public static class SPTExt
    {
        public static string PrettyPrint(this List<InputParameter> inputParameters, List<OutputParameter> outputParameters)
        {
            return String.Format("{0}, {1}", PrettyPrint(inputParameters), PrettyPrint(outputParameters));
        }

        public static string PrettyPrint(this IEnumerable<InputParameter> inputParameters)
        {
            var inP = "(none)";

            if (inputParameters.AnyItems())
                inP = inputParameters
                    .Select(p => String.Format("[{0}]{1}={2}{3}", p.DataType, p.Parameter, !p.Binding.IsNullOrWhitespace() ? p.Binding.Replace('{', '[').Replace('}', ']') + "=" : "", p.Value ?? "(not set)"))
                    .Aggregate((p1, p2) => String.Format("{0}{1}", p1, p2));

            return String.Format("In=>{0}", inP);
        }

        public static string PrettyPrint(this IEnumerable<OutputParameter> outputParameters)
        {
            var outP = "(none)";

            if (outputParameters.AnyItems())
                outP = outputParameters
                    .Select(p => String.Format("[{0}]{1}={2}", p.DataType, p.Parameter, p.Value ?? "(not set)"))
                    .Aggregate((p1, p2) => String.Format("{0}{1}", p1, p2));

            return String.Format("Out=>{0}", outP);
        }
    }
}

