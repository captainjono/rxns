using Rxns.Metrics;

namespace Rxns.Hosting
{
    public class StaticFileSystemConfiguration : IFileSystemConfiguration
    {
        public StaticFileSystemConfiguration(string temptDir = "temp")
        {
            TemporaryDirectory = temptDir;
        }
        public string TemporaryDirectory { get; private set; }
    }
}
