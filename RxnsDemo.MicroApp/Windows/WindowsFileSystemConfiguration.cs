

using Rxns.Metrics;

namespace System.IO
{
    public class WindowsFileSystemConfiguration : IFileSystemConfiguration
    {
        public string TemporaryDirectory { get { return Path.GetTempPath(); } }
    }

}
