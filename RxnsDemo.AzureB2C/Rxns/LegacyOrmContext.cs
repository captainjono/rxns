using System.Data;

namespace RxnsDemo.AzureB2C.Rxns
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
