//using System;
//using System.Collections.Generic;
//using System.Data;
//using System.IO;
//using System.Threading.Tasks;
//using Microsoft.WindowsAzure.Storage.Blob;
//using Microsoft.WindowsAzure.Storage.RetryPolicies;
//using Rxns.Hosting.Updates;

//namespace Rxns.Azure
//{
//    public class AzureUpdateStorageClient : IUpdateStorageClient
//    {
//        private const string Prefix = "updates";
//        private readonly IAzureServiceFactory _azureServiceFactory;

//        public AzureUpdateStorageClient(IAzureServiceFactory azureFactory)
//        {
//            _azureServiceFactory = azureFactory;
//        }
//        private ICloudBlobContainer GetUpdateContainerForSystem(string systemName)
//        {
//            return _azureServiceFactory.GetBlobContainer(GetContainerName(systemName));
//        }

//        public async Task<bool> CreateUpdate(string systemName, string version, Stream update)
//        {
//            var container = GetUpdateContainerForSystem(systemName);
//            var versionZip = container.GetBlockBlobReference(GetBlobName(version));
            
//            if (versionZip.Exists(GetRelabilityOptions()))
//                throw new DuplicateNameException(String.Format("Version '{0}' already exists", version));

//            await versionZip.UploadFromStreamAsync(update, null, GetRelabilityOptions(), null);

//            return true;
//        }

//        public async Task<Stream> GetUpdate(string systemName, string version = null)
//        {
//            var container = GetUpdateContainerForSystem(systemName);
//            var blobName = version != null ? GetBlobName(version) : container.ListBlobs().OrderByDescending(b => b.Uri).Select(s => s.Uri.LocalPath).FirstOrDefault();
//            if(blobName == null)
//                throw new FileNotFoundException("No updates are availible for this system");

//            var versionZip = container.GetBlockBlobReference(blobName);
//            var versionStream = new MemoryStream();

//            if (!versionZip.Exists())
//                throw new FileNotFoundException(String.Format("Version '{0}' of '{1}' does not exist", version ?? "latest", systemName));

//            await versionZip.DownloadToStreamAsync(versionStream, null, GetRelabilityOptions(), null);

//            return versionStream;
//        }

//        public string GetContainerName(string systemName)
//        {
//            return string.Format("{0}-{1}", Prefix, systemName.Replace(" ", ""));
//        }

//        public string GetBlobName(string version)
//        {
//            return string.Format("{0}.zip", version.Replace("-", ""));
//        }

//        private BlobRequestOptions GetRelabilityOptions()
//        {
//            return new BlobRequestOptions()
//            {
//                RetryPolicy = new LinearRetry(TimeSpan.FromSeconds(2), 3),
//                ServerTimeout = TimeSpan.FromMinutes(2)
//            };
//        }

//        public async Task DeleteAllUpdates(string systemName)
//        {
//            await GetUpdateContainerForSystem(systemName).DeleteAsync();
//        }

//        public IEnumerable<string> ListUpdates(string systemName, int top = 3)
//        {
//            return GetUpdateContainerForSystem(systemName).ListBlobs().OfType<ICloudBlob>().OrderByDescending(b => b.Properties.LastModified).Take(top).Select(s => s.Name);
//        }
//    }
//}
