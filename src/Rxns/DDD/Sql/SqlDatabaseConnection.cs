using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Reactive;
using Autofac.Features.OwnedInstances;
using Rxns.Interfaces.Reliability;
using Rxns.Scheduling;

namespace Rxns.DDD.Sql
{
    public class TenantDataRecord : IDataRecordWithContext
    {
        public string Database { get; set; }

        public IRecord Data { get; set; }
    }

    public class SqlRecord : IRecord
    {
        private readonly IDataRecord _dataRecord;

        public SqlRecord(IDataRecord dataRecord)
        {
            _dataRecord = dataRecord;
        }

        public object this[string name]
        {
            get { return _dataRecord[name]; }
        }

        public bool IsDBNull(int columnIndex)
        {
            return _dataRecord.IsDBNull(columnIndex);
        }

        public string GetString(int columnIndex)
        {
            return _dataRecord.GetString(columnIndex);

        }
    }


    public class SqlDatabaseConnection : IDatabaseConnection
    {
        private readonly Func<string, Owned<IDbConnection>> _connectionFactory;
        private IReliabilityManager _reliably;

        public SqlDatabaseConnection(Func<string, Owned<IDbConnection>> connectionFactory, IReliabilityManager reliably)
        {
            _reliably = reliably;
            _connectionFactory = connectionFactory;
        }

        private string ChangeDatabase(string connectionString, string database)
        {
            if (String.IsNullOrWhiteSpace(database))
                return connectionString;

            var cs = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = database.RemoveSqlQualification() };
            return cs.ToString();
        }

        public IObservable<IEnumerable<OutputParameter>> ExecuteStoredProcedure(string connectionString, string name, IEnumerable<InputParameter> inputParameters = default(IEnumerable<InputParameter>), IEnumerable<OutputParameter> outputParameters = default(IEnumerable<OutputParameter>), string database = null)
        {
            //get parameters
            var input = ConvertToSqlInputParameters(inputParameters);
            var output = ConvertToSqlOutputParameters(outputParameters);
            connectionString = ChangeDatabase(connectionString, database);

            return _reliably.CallDatabase(() =>
            {
                using (var connection = _connectionFactory(connectionString))
                {
                    var cmd = SetupCommand(connection.Value.CreateCommand());
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.CommandText = name;
                    cmd.Parameters.AddRange(input);
                    cmd.Parameters.AddRange(output);

                    var returnValue = GetReturnValueParameter();
                    cmd.Parameters.Add(returnValue);

                    connection.Value.Open();


                    cmd.ExecuteNonQuery();

                    return UpdateOutputValues(name, output, outputParameters, (int)(returnValue.Value ?? "0"));
                }
            });
        }

        private IDbCommand SetupCommand(IDbCommand command)
        {
            command.CommandTimeout = 0;

            return command;
        }

        public IObservable<IEnumerable<OutputParameter>> ExecuteStoredProcedureWithResults(string connectionString, string name, Action<IDataRecordWithContext> foreachDataRow, IEnumerable<InputParameter> inputParameters = default(IEnumerable<InputParameter>), IEnumerable<OutputParameter> outputParameters = default(IEnumerable<OutputParameter>), string database = null)
        {
            //get parameters
            var input = ConvertToSqlInputParameters(inputParameters);
            var output = ConvertToSqlOutputParameters(outputParameters);
            connectionString = ChangeDatabase(connectionString, database);

            return _reliably.CallDatabase(() =>
            {
                using (var connection = _connectionFactory(connectionString))
                {
                    var rowCount = 0;
                    var context = connection.Value;

                    var cmd = SetupCommand(context.CreateCommand());
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.CommandText = name;
                    cmd.Parameters.AddRange(input);
                    cmd.Parameters.AddRange(output);

                    context.Open();

                    var data = cmd.ExecuteReader();

                    while (data.Read())
                    {
                        rowCount++;
                        foreachDataRow(new TenantDataRecord() { Database = database, Data = new SqlRecord(data) });
                    }

                    return UpdateOutputValues(name, output, outputParameters, rowCount);
                }
            });
        }



        public Owned<IDbTransaction> BeginTransaction(string connectionString, string database = null)
        {
            var connection = _connectionFactory(ChangeDatabase(connectionString, database)).Value;
            connection.Open();

            return new Owned<IDbTransaction>( connection.BeginTransaction(), connection);
        }

        public IObservable<Unit> ExecuteScript(string script, IDbTransaction scope)
        {
            return _reliably.CallDatabase(() =>
            {
                var cmd = SetupCommand(scope.Connection.CreateCommand());
                cmd.CommandType = CommandType.Text;
                cmd.CommandText = script;
                cmd.Transaction = scope;

                cmd.ExecuteNonQuery();
            });
        }

        public IObservable<Unit> ExecuteScript(string connectionString, string script, string database = null)
        {
            connectionString = ChangeDatabase(connectionString, database);

            return _reliably.CallDatabase(() =>
            {
                using (var connection = _connectionFactory(connectionString))
                {
                    var context = connection.Value;

                    var cmd = SetupCommand(context.CreateCommand());
                    cmd.CommandType = CommandType.Text;
                    cmd.CommandText = script;

                    context.Open();
                    cmd.Connection = context;

                    cmd.ExecuteNonQuery();
                }
            });
        }

        private SqlParameter GetReturnValueParameter()
        {
            return (new SqlParameter("@__return", SqlDbType.Int)
            {
                Direction = ParameterDirection.ReturnValue,
                Size = 4000
            });
        }

        private SqlParameter[] ConvertToSqlOutputParameters(IEnumerable<OutputParameter> outputParameters)
        {
            if (outputParameters == null)
                return new SqlParameter[] { };

            return outputParameters.Where(o => !String.IsNullOrEmpty(o.DataType)).Select(ip =>
                new SqlParameter(ip.Parameter, (SqlDbType)Enum.Parse(typeof(SqlDbType), ip.DataType, true))
                {
                    Direction = ParameterDirection.Output,
                    Size = ip.DataType.Contains("char", StringComparison.OrdinalIgnoreCase) ? 4000 : 0
                }).ToArray();
        }

        private SqlParameter[] ConvertToSqlInputParameters(IEnumerable<InputParameter> inputParameters)
        {
            if (inputParameters == null)
                return new SqlParameter[] { };

            return inputParameters.Where(i => !String.IsNullOrEmpty(i.DataType)).Select(ip =>
                new SqlParameter(ip.Parameter, ip.Value)
                {
                    Direction = ParameterDirection.Input,
                    SqlDbType = String.IsNullOrEmpty(ip.DataType) ? SqlDbType.NVarChar : (SqlDbType)Enum.Parse(typeof(SqlDbType), ip.DataType, true)
                }).ToArray();
        }

        private IEnumerable<OutputParameter> UpdateOutputValues(string name, SqlParameter[] output, IEnumerable<OutputParameter> outputParameters, object returnValue)
        {
            if (outputParameters == null)
                outputParameters = new List<OutputParameter>();

            foreach (var o in output)
            {
                //its ok, we know the lists will always match up
                outputParameters.FirstOrDefault(p => p.Parameter == o.ParameterName).Value = o.Value;
            }

            return outputParameters.Concat(new[] { new OutputParameter() { Name = String.Format("__return_{0}", RemoveQualification(name)), Value = returnValue } });
        }

        private string RemoveQualification(string name)
        {
            try
            {
                return name.TrimEnd(']').Substring(name.LastIndexOf('[') + 1);
            }
            catch (Exception)
            {
                return name;
            }
        }

    }

    internal static class SqlExtensions
    {
        internal static void AddRange(this IDataParameterCollection collection, IEnumerable<SqlParameter> commands)
        {
            foreach (var cmd in commands)
                collection.Add(cmd);
        }
    }
}
