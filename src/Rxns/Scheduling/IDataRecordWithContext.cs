namespace Rxns.Scheduling
{
    public interface IDataRecordWithContext
    {
        IDatabaseConnection Connection { get; }
    }
}