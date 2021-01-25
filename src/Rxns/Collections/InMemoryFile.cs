using System;
using System.IO;
using Rxns.Interfaces;

namespace Rxns.Collections
{
    public class InMemoryFile : IFileMeta
    {
        public Stream Contents { get; set; }

        public Stream Contents1 { get; set; }

        public string ContentType { get; set; }

        public string Fullname { get; set; }

        public string Hash { get; set; }

        public DateTime LastWriteTime { get; set; }

        public long Length { get; set; }

        public string Name { get; set; }

        public InMemoryFile()
        {
            ContentType = "application/octet";
            Length = 0;
        }
    }
}
