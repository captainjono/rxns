

using Rxns.Metrics;

namespace System.IO
{
    public class DotNetFileSystemConfiguration : IFileSystemConfiguration
    {
        public string TemporaryDirectory { get { return Path.GetTempPath(); } }
    }

}
