namespace Rxns.Interfaces
{
    public interface IKeyValueStoreFactory
    {
        IKeyValueStore<string, TValue> GetOrCreate<TValue>(string name, string partition = null);
    }
}
