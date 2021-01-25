using System;

namespace Rxns.Hosting
{
    public interface IAppSetup : IDisposable
    {
        TimeSpan Timeout { get; set; }

        void Install();

        void Uninstall();
    }
}
