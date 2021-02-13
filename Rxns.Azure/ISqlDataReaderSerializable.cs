using System.Data;

namespace Rxns.Azure
{
    public interface ISqlDataReaderSerializable
    {
        void Serialize(IDataRecord record);
    }
}
