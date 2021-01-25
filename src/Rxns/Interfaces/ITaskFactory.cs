namespace Rxns.Interfaces
{
    public interface ITaskFactory
    {
        T Get<T>(string taskName) where T : IReportStatus;
    }
}
