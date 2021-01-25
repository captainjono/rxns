using System.IO;
using Autofac;
using Rxns.Health;
using Rxns.Hosting;

namespace Rxns.Windows
{
    public class WindowsModule : IAppModule
    {
        public IRxnLifecycle Load(IRxnLifecycle lifecycle)
        {
            return lifecycle
                .CreatesOncePerApp<DotNetFileSystemConfiguration>()
                .CreatesOncePerApp<DotNetFileSystemService>()
                .CreatesOncePerApp<WindowsSystemInformationService>()
                .CreatesOncePerApp<WindowsSystemServices>();
        }
    }
}
