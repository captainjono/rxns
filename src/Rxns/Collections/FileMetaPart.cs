using Rxns.Interfaces;
using System;
using System.IO;


namespace Rxns.WebApi.Server.IO
{
    public class FileMetaPart : IFileMeta
    {
        private readonly IFileMeta _originalFile;
        public string FileHash { get { return _originalFile.Hash; } set { } }
        public Guid Id { get; set; }

        public Stream Contents { get; set; }

        public Stream Contents1 { get; set; }

        public string Fullname { get { return _originalFile.Fullname; } set { } }
        public string ContentType { get { return _originalFile.ContentType; } set { } }
        public string Hash { get; set; }
        public DateTime LastWriteTime { get; set; }
        public long Length { get { return _originalFile.Length; } }
        public string Name { get { return _originalFile.Name; } }
        public byte[] Md5 { get; set; }

        private FileMetaPart(IFileMeta originalFile)
        {
            _originalFile = originalFile;
        }

        public static FileMetaPart FromFileMeta(IFileMeta originalFile, byte[] chunk = null)
        {
            var part = new FileMetaPart(originalFile);
            part.Id = Guid.NewGuid();

            if (chunk != null)
            {
                part.Contents = chunk.ToStream();
            }
            else
            {
                part.Hash = originalFile.Hash;
            }
            
            part.LastWriteTime = originalFile.LastWriteTime;

            return part;
        }
    }
}
