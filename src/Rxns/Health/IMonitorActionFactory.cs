namespace Rxns
{
    public interface IMonitorActionFactory<T>
    {
        MonitorAction<T> Before();
        MonitorAction<T> After();
    }
}
