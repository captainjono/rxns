using System.Data;
using Rxns.DDD.Sql;

namespace Rxns.DDD
{
    public class LegacyOrmContext : SqlOrmDbContext
    {
        public LegacyOrmContext(string connectionString) : base(connectionString)
        {

        }
        public override void OnConnectionEstablished(IDbConnection context)
        {
            
        }
    }
}

