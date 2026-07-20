using System;
using System.IO;

namespace Rxns.Interfaces
{
    public interface IFileMeta
    {
        Stream Contents { get; set; }
        string ContentType { get; set; }
        string Fullname { get; set; }
        string Hash { get; set; }
        DateTime LastWriteTime { get; set; }
        long Length { get; }
        string Name { get; }
    }
}
