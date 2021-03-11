using System;
using System.IO;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Rxns;
using Rxns.Collections;
using Rxns.Interfaces;
using Rxns.WebApi.Compression;

namespace RxnsDemo.AzureB2C.Rxns.Tenant
{
    public static class FileMetaExtensions
    {
        public static IObservable<byte[]> ReadInChunks(this Stream file, int bufferSize)
        {
            return Observable.Create<byte[]>(o =>
            {
                var buffer = new byte[bufferSize];
                var bytes = bufferSize;

                while (bytes == bufferSize)
                {
                    bytes = file.ReadAsync(buffer, 0, buffer.Length).WaitR();
                    o.OnNext(buffer.Length == bytes ? buffer : buffer.Take(bytes).ToArray());
                }

                o.OnCompleted();
                return Disposable.Empty;
            });
        }
        
        public static IFileMeta Gzip(this IFileMeta meta)
        {
            var compressedContent = new MemoryStream();
            var compressed = new InMemoryFile();
            var compressor = new GZipCompressor();
            compressor.Compress(meta.Contents, compressedContent);

            compressed.Fullname = meta.Fullname;
            compressed.Name = meta.Name;
            compressed.ContentType = meta.ContentType;
            compressed.Contents = compressedContent;
            compressed.Hash = meta.Hash;
            compressed.LastWriteTime = meta.LastWriteTime;

            return compressed;
        }
    }
}
