namespace Rxns.Hosting
{
    public interface IRxnAppInfo
    {
        string Name { get; }
        string Version { get; }
        string Url { get; }
        string Id { get; }
        bool KeepUpdated { get; }
    }
}
