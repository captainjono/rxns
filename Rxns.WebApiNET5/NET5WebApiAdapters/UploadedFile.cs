using System;
using System.IO;
using System.IO.Compression;
using Rxns.Interfaces;

namespace Rxns.WebApiNET5.NET5WebApiAdapters
{
    public class UploadedFile : IFileMeta
    {
        private readonly string _tempLocation;
        private Stream _tempContent;
        public readonly IFileMeta OriginalFile;
        private readonly bool _isCompressed = false;

        public UploadedFile(IFileMeta original, string tempLocation = null, bool isCompressed = false)
        {
            OriginalFile = original;
            _tempLocation = tempLocation;
            _isCompressed = isCompressed;
        }

        public UploadedFile(IFileMeta original)
        {
            OriginalFile = original;
        }

        public Stream Contents1
        {
            get { throw new NotImplementedException(); }
            set { throw new NotImplementedException(); }
        }

        public string ContentType
        {
            get { return OriginalFile.ContentType; }
            set { OriginalFile.ContentType = value; }
        }

        public Stream Contents
        {
            get
            {
                if (StringExtensions.IsNullOrWhitespace(_tempLocation)) return OriginalFile.Contents;

                if (_tempContent == null)
                {
                    if (_isCompressed)
                    {
                        using (var tmp = new GZipStream(File.OpenRead(_tempLocation), CompressionMode.Decompress))
                        {
                            _tempContent = new MemoryStream();
                            tmp.CopyTo(_tempContent);
                            _tempContent.Seek(0, SeekOrigin.Begin);
                        }
                    }
                    else
                    {
                        _tempContent = File.OpenRead(_tempLocation);
                    }
                }

                return _tempContent;
            }
            set
            {
                if (StringExtensions.IsNullOrWhitespace(_tempLocation)) throw new ArgumentNullException("tempLocation");
                File.WriteAllBytes(_tempLocation, value.ToBytes());
            }
        }

        public string Fullname
        {
            get { return OriginalFile.Fullname; }
            set { OriginalFile.Fullname = value; }
        }

        public string Hash
        {
            get { return OriginalFile.Hash; }
            set { OriginalFile.Hash = value; }
        }

        public DateTime LastWriteTime
        {
            get { return OriginalFile.LastWriteTime; }
            set { OriginalFile.LastWriteTime = value; }
        }

        public long Length
        {
            get { return new FileInfo(_tempLocation).Length; }
            set { throw new InvalidOperationException("Cannot set the length of a real file"); }
        }

        public string Name
        {
            get { return OriginalFile.Name; }
        }

        public Stream Open()
        {
            return Contents;
        }
        public IFileMeta Meta { get { return this; } }

        public void Delete()
        {
            if (_tempContent != null)
                _tempContent.Dispose();

            if (!StringExtensions.IsNullOrWhitespace(_tempLocation))
                File.Delete(_tempLocation);

        }
}

    public static class IoExtensions
    {
        /// <summary>
        ///     A Stream extension method that converts the Stream to a byte array.
        /// </summary>
        /// <param name="this">The Stream to act on.</param>
        /// <returns>The Stream as a byte[].</returns>
        public static byte[] ToBytes(this Stream @this)
        {
            using (var ms = new MemoryStream())
            {
                @this.CopyTo(ms);
                return ms.ToArray();
            }
        }
    }
}
