using System;
using System.Collections.Generic;
using System.Data;
using System.Reactive;
using Autofac.Features.OwnedInstances;

namespace Rxns.Scheduling
{
    public class RxSqlException : Exception
    {
        public int Number { get; set; }
    }

    public interface IDefaultDbCfg
    {
        string ConnectionString { get; }
    }

    public interface IDatabaseConnection
    {
        IObservable<IEnumerable<OutputParameter>> ExecuteStoredProcedure(string connectionString, string name,
                                                            IEnumerable<InputParameter> inputParameters = default(IEnumerable<InputParameter>),
                                                            IEnumerable<OutputParameter> outputParameters = default(IEnumerable<OutputParameter>), 
                                                            string database = null);
        
        IObservable<Unit> ExecuteScript(string connectionString, string script, string database = null);
        IObservable<Unit> ExecuteScript(string script, IDbTransaction database);

        Owned<IDbTransaction> BeginTransaction(string connectionString, string database);
    }
}
