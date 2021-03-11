using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading.Tasks;
using Rxns;
using Rxns.Interfaces;
using Rxns.Metrics;

namespace RxnsDemo.AzureB2C.Rxns.Tenant
{
    public class TemporaryDirectoryTenantFileStorage : ITenantFileStorage
    {
        private readonly IFileSystemConfiguration _fsConfiguration;
        private readonly IFileSystemService _fsService;
        public int ReadWriteBuffer { get; set; }

        public TemporaryDirectoryTenantFileStorage(IFileSystemConfiguration fsConfiguration, IFileSystemService fsService)
        {
            _fsConfiguration = fsConfiguration;
            _fsService = fsService;
        }

        private string GetTenantDirectory(string tenantId)
        {
            var dir = _fsService.PathCombine(_fsConfiguration.TemporaryDirectory, tenantId);
            _fsService.CreateDirectory(_fsService.PathCombine(_fsConfiguration.TemporaryDirectory, tenantId));
            return dir;
        }

        public Task<Stream> GetAsync(string tenantId, string location, string fileName)
        {
            return _fsService.GetReadableFile(GetTenantDirectory(tenantId), location, fileName).ToResult();
        }


        public Task<IFileMeta[]> GetAllAsync(string tenantId, string location)
        {
            return _fsService.GetFiles(GetTenantDirectory(tenantId), location).ToArray().ToResult();
        }

        public Task<IFileMeta> SaveAsync(string tenantId, string location, IFileMeta file, SeekOrigin offset = SeekOrigin.Begin)
        {
            return Rxn.Create(() =>
            {
                var stream = _fsService.CreateWriteableFile(GetTenantDirectory(tenantId), location, file.Name);
                stream.Seek(0, offset);
                file.Contents.ReadInChunks(ReadWriteBuffer).Do(bytes => stream.Write(bytes, 0, bytes.Length)).Wait();
                return file;
            }).ToTask();
        }

        public Task<IFileMeta> GetMeta(string tenantId, string location, string fileName)
        {
            return ((IFileMeta)new ReadonlyFileMeta(new FileInfo(_fsService.PathCombine(GetTenantDirectory(tenantId), location, fileName)))).ToResult();
        }

        public Task<bool> DeleteAsync(string tenantId, string location, string fileName)
        {
            _fsService.DeleteFile(GetTenantDirectory(tenantId), location, fileName);
            return true.ToResult();
        }
    }
}
