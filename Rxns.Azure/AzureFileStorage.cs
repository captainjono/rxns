//using System;
//using System.IO;
//using System.Reactive.Threading.Tasks;
//using System.Threading.Tasks;
//using Microsoft.WindowsAzure.Storage.Blob;
//using Rxns.Interfaces;

//namespace Rxns.Azure
//{
//    public class AzureFileStorage : ITenantFileStorage
//    {
//        protected readonly IAzureServiceFactory _azureServiceFactory;
//        private readonly bool _createAsPublic;

//        public AzureFileStorage(IAzureServiceFactory azureServiceFactory, bool createAsPublic = false)
//        {
//            _createAsPublic = createAsPublic;
//            _azureServiceFactory = azureServiceFactory;
//        }

//        /// <summary>
//        /// 
//        /// </summary>
//        /// <param name="folder"></param>
//        /// <param name="tenantId">If null, the folder will be the container name only</param>
//        /// <returns></returns>
//        protected ICloudBlobContainer GetContainer(string folder, string tenantId)
//        {
//            var path = tenantId == null ? folder : String.Format("{0}-{1}", folder, tenantId);

//            return _azureServiceFactory.GetBlobContainer(path, _createAsPublic);
//        }

//        public async Task<Stream> GetAsync(string tenantId, string location, string fullName)
//        {
//            var contents = new MemoryStream();
//            var container = GetContainer(location, tenantId);
//            var pendingDocument = container.GetBlockBlobReference(fullName);

//            if (!pendingDocument.Exists())
//                throw new Exception(String.Format("Tenant '{0}' has no document named '{1}'", tenantId, fullName));

//            await pendingDocument.DownloadToStreamAsync(contents, null, AzureHelper.GetRelabilityOptions(), null);
//            contents.Position = 0;

//            return contents;
//        }

//        public Task<IFileMeta[]> GetAllAsync(string tenantId, string location)
//        {
//            return Rxn.Create(() =>
//            {
//                var container = GetContainer(location, tenantId);

//                return container.ListBlobs().OfType<ICloudBlob>().Select(s => s.AsMeta()).ToArray();
//            }).ToTask();
//        }

//        public Task<IFileMeta> SaveAsync(string tenantId, string location, IFileMeta file, SeekOrigin offset = SeekOrigin.Begin)
//        {
//            var container = GetContainer(location, tenantId);
//            var destination = container.GetBlockBlobReference(file.Name);

//            return ReliablyUpload(destination, file, file.Contents);
//        }


//        public async Task<IFileMeta> GetMeta(string tenantId, string location, string fullName)
//        {
//            if (string.IsNullOrWhiteSpace(tenantId)) throw new ArgumentException("tenantId");
//            if (string.IsNullOrWhiteSpace(fullName)) throw new ArgumentException("fileName");

//            var container = GetContainer(location, tenantId);
//            var file = container.GetBlockBlobReference(fullName);
//            if (!await file.ExistsAsync()) throw new FileNotFoundException(fullName);

//            var metadata = await file.GetMetadata();
//            return metadata;
//        }


//        public Task<bool> DeleteAsync(string tenantId, string location, string fullName)
//        {
//            if (fullName.IsNullOrWhitespace()) throw new ArgumentException("fullName", "must specify file to delete");

//            var container = GetContainer(location, tenantId);
//            return container.GetBlockBlobReference(fullName).DeleteIfExistsAsync();
//        }
        
//        protected async Task<IFileMeta> ReliablyUpload(ICloudBlob destination, IFileMeta file, Stream contents)
//        {
//            await destination.UploadFromStreamAsync(contents, null, AzureHelper.GetRelabilityOptions(), null);
//            destination.SetMetadataAsync(file);

//            return destination.AsMeta();
//        }

//        public string GetTemporaryUrl(string tenantId, string location, IFileMeta file, TimeSpan? urlTTL = null)
//        {
//            urlTTL = urlTTL ?? TimeSpan.FromDays(1);
//            var container = GetContainer(location, tenantId);
//            var fileReference = container.GetBlockBlobReference(file.Fullname);

//            if (!fileReference.Exists(AzureHelper.GetRelabilityOptions())) throw new FileNotFoundException(String.Format("'{0}' has no document '{1}'", tenantId, file.Fullname), file.Fullname);

//            return fileReference.CreateTemporaryUrl(urlTTL);
//        }

//        public ICloudBlob GetUniqueBlob(Microsoft.Azure.Storage.Blob. container, string fileName)
//        {
//            if (fileName.IsNullOrWhitespace()) throw new ArgumentException(fileName, "Cannot find unique name for null document name");

//            var extension = Path.GetExtension(fileName);

//            if (fileName.Length >= 260)
//                throw new PathTooLongException(String.Format("The document name specified '{0}' is to long", fileName));
//            int i = 0;

//            while (true)
//            {
//                var uniqueName = String.Format("{0}{2}{1}", fileName.Substring(0, fileName.Length - extension.Length), extension, i++ > 0 ? " (" + i + ")" : "");
//                var file = container.getblGetBlockBlobReference(uniqueName);

//                if (!file.Exists(AzureHelper.GetRelabilityOptions()))
//                    return file;
//            }
//        }
//    }
//}
