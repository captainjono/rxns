namespace Rxns.Hosting
{
    public interface IRxnAppInfo
    {
        string Name { get; }
        string Version { get; set; }
        string Url { get; }
        string Id { get; }
        bool KeepUpdated { get; }
    }
}
