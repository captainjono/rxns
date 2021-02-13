using System;
using System.IO;
using Microsoft.WindowsAzure.Storage.Blob;
using Rxns.Interfaces;

namespace Rxns.Azure
{
    public class AzureFileMeta : IFileMeta
    {
        private Stream _contents = null;
        public Stream Contents
        {
            get { return _contents = _contents ?? DownloadIntoMemory(); }
            set { throw new NotSupportedException("Writing to azure blob not supported"); }
        }

        public Stream Contents1
        {
            get { throw new NotImplementedException(); }
            set { throw new NotImplementedException(); }
        }

        public string ContentType { get; set; }
        public string Fullname { get; set; }
        public string Hash { get; set; }
        public DateTime LastWriteTime { get; set; }
        public long Length { get; private set; }
        public string Name { get; private set; }

        public ICloudBlob Blob { get; private set; }

        public AzureFileMeta(ICloudBlob blob)
        {
            Blob = blob;
            Fullname = blob.Name;
            Name = Path.GetFileName(blob.Name);
            ContentType = blob.Properties.ContentType;

            try
            {
                Hash = blob.Properties.ContentMD5.FromBase64().ToHash();
            }
            catch (Exception e)
            {
#if DEBUG
                Console.WriteLine(e);
#endif
                Hash = null;
            }
        }

        private MemoryStream DownloadIntoMemory()
        {
            var contents = new MemoryStream();
            Blob.DownloadToStreamAsync(contents, null, AzureHelper.GetRelabilityOptions(), null).Wait();
            contents.Position = 0;
            Length = contents.Length;

            return contents;
        }

        public override string ToString()
        {
            return Name ?? "(No filename specified)";
        }
    }
}