using System.Data;

namespace Rxns.Scheduling
{
    public interface IDataRecordWithContext
    {
        string Database { get; }
        IRecord Data { get; set; }
    }


    public interface IRecord
    {
        string GetString(int columnIndex);
        bool IsDBNull(int columnIndex);
        object this[string name] { get; }
    }
}