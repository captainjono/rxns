using System.IO;
using Rxns.Hosting.Updates;
using Rxns.Metrics;

namespace Rxns.Hosting
{
    public class StaticFileSystemConfiguration : IFileSystemConfiguration
    {
        public StaticFileSystemConfiguration(IAppStatusCfg cfg)
        {
            TemporaryDirectory = Path.Combine(cfg.AppRoot, ".temp");
        }
        public string TemporaryDirectory { get; private set; }
    }
}
