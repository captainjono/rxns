namespace Rxns.Interfaces
{
    public interface IContainerPostBuildService
    {
        void Run(IReportStatus logger, IResolveTypes container);
    }
}
