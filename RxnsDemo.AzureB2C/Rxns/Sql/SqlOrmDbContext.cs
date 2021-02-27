using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;    

namespace RxnsDemo.AzureB2C.Rxns
{
    public abstract class SqlOrmDbContext : IOrmTransactionContext
    {
        /// <summary>
        /// The connectionstring for the support database
        /// </summary>
        public string ConnectionString { get; set; }

        
        protected readonly List<IDisposable> _resources = new List<IDisposable>();
        private SqlConnection _db;


        protected SqlOrmDbContext(string connectionString = null)
        {
            ConnectionString = connectionString;
        }
        
        private void Connect()
        {
            if (_db == null)
            {
                _db = new SqlConnection(ConnectionString);

                _db.Open();
                OnConnectionEstablished(_db);
            }
        }

        public abstract void OnConnectionEstablished(IDbConnection context);

        public virtual T Run<T>(Func<IDbConnection, T> task)
        {
            Connect();

            return task(_db);
        }

        public virtual IOrmBatchContext StartBatch()
        {
            Connect();

            return new OrmBatchContext(_db);
        }

        public virtual void Run(Action<IDbConnection> task)
        {
            Connect();

            task(_db);
        }
    }
}
