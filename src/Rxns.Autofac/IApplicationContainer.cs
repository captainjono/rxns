using Autofac;
using Rxns.Interfaces;

namespace Rxns.Autofac
{
    public interface IApplicationContainer : IReportStatus
    {
        void Build();
        IContainer Container { get; }
    }
}
