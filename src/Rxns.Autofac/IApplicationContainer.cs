using Autofac;
using Rxns.Interfaces;

namespace Rxns
{
    public interface IApplicationContainer : IReportStatus
    {
        void Build();
        IContainer Container { get; }
    }
}
