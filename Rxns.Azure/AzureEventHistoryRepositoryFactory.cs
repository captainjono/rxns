//using Microsoft.WindowsAzure.Storage;
//using Rxns.DDD.BoundedContext;
//using Rxns.Interfaces;
//using Rxns.Playback;

//namespace Rxns.Azure
//{
//    public class AzureEventHistoryRepositoryFactory : 
//    {
//        private readonly IResolveTypes _resolver;
//        private readonly IAzureConfiguration _configuration;

//        public AzureEventHistoryRepositoryFactory(IResolveTypes resolver, IAzureConfiguration configuration)
//        {
//            _resolver = resolver;
//            _configuration = configuration;
//        }

//        public ITenantModelRepository<EventHistory> Create(string tableName)
//        {
//            //todo: create abstraction that hides the table service behind ITableStorage interface? so no CloudStorageAccount APi is accessed from this class
//            //and other classes will be able to use the API without polluting the solution with hard references
//            var storageAccount = CloudStorageAccount.Parse(_configuration.StorageConnectionString);
//            var client = storageAccount.CreateCloudTableClient();
//            var table = client.GetTableReference(tableName);
//            table.CreateIfNotExistsAsync().WaitR();

//            return new AzureTableEventSourcingRepository<EventHistory>(table, _resolver);
//        }
//    }
//}

