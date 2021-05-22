using System;
using System.Data;

namespace Rxns.DDD
{
    public interface IOrmTransactionContext : IOrmContext
    {
        IOrmBatchContext StartBatch();
    }

    public interface IOrmContext
    {
        T Run<T>(Func<IDbConnection, T> task);

        void Run(Action<IDbConnection> task);
    }

    public interface IOrmBatchContext : IOrmContext, IDisposable
    {
        IDbConnection Connection { get; }
    }

    public class OrmBatchContext : IOrmBatchContext, IOrmContext, IDisposable
    {
        public IDbConnection Connection { get; private set; }

        public OrmBatchContext(IDbConnection connection)
        {
            this.Connection = connection;
        }

        public T Run<T>(Func<IDbConnection, T> task)
        {
            return task(this.Connection);
        }

        public void Run(Action<IDbConnection> task)
        {
            task(this.Connection);
        }

        public void Dispose()
        {
            this.Connection.Dispose();
        }
    }

}
