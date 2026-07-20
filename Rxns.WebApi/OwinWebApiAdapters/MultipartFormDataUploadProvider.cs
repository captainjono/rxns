using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Rxns.Hosting.Updates;
using Rxns.Interfaces;
using Rxns.Logging;
using Rxns.Metrics;
using Rxns.WebApi.Server.IO;

namespace Rxns.WebApi
{
    public class MultipartFormDataUploadProvider : ReportsStatus, IFileUploadProvider
    {
        public IScheduler DefaultScheduler { get; set; }
        protected readonly IFileSystemService _fileSystem;

        protected readonly IFileSystemConfiguration FsConfiguration;
        public MultipartFormDataUploadProvider(IFileSystemConfiguration fsConfiguration, IFileSystemService fileSystem, IScheduler scheduler = null)
        {
            FsConfiguration = fsConfiguration;
            _fileSystem = fileSystem;

            DefaultScheduler = scheduler ?? Scheduler.Immediate;
        }

        public IObservable<UploadedFile> GetFiles(HttpRequestMessage request)
        {
            return Observable.Create<UploadedFile>(o =>
            {
                try
                {
                    var streamProvider = new MultipartFormDataStreamProvider(FsConfiguration.TemporaryDirectory);
                    return request.Content.ReadAsMultipartAsync(streamProvider).ToObservable().Subscribe(content =>
                    {
                        OnInformation("Files have been uploaded to disk, getting list of files now");
                        if (!content.FileData.Any())
                        {
                            o.OnError(new FileUploadException("No uploaded file(s) found"));
                            return;
                        }

                        content.FileData
                            .ToObservable()
                            .ObserveOn(DefaultScheduler)
                            .Select(file => new UploadedFile(ReadAsFileMeta(file.Headers), _fileSystem.PathCombine(FsConfiguration.TemporaryDirectory, file.LocalFileName), file.Headers.ContentEncoding.Any(c => c.Contains("gzip"))))
                        .Subscribe(o);
                    }, o.OnError);

                }
                catch (Exception e)
                {
                    o.OnError(e);

                    return Disposable.Empty;
                }
            });
        }

        private IFileMeta ReadAsFileMeta(HttpContentHeaders content)
        {
            var file = _fileSystem.ToFileMeta(
                content.ContentDisposition.FileName != null ? content.ContentDisposition.FileName.Replace("\"", "") : "",
                content.ContentType != null ? content.ContentType.MediaType : "",
                content.ContentDisposition.CreationDate.HasValue ? content.ContentDisposition.CreationDate.Value.UtcDateTime : DateTime.UtcNow
            );

            //check to see if its a filepart
            var partFlag = content.ContentDisposition.Parameters.FirstOrDefault(p => p.Name == "PartId");

            if (partFlag == null)
            {
                file.Hash = content.ContentMD5 != null ? content.ContentMD5.ToHash() : "";
                return file;
            }

            var part = FileMetaPart.FromFileMeta(file);
            part.Id = Guid.Parse(partFlag.Value);
            part.Md5 = content.ContentMD5;

            return part;
        }
    }
}
