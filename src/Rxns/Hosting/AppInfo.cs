namespace Rxns.Hosting
{
    public class AppVersionInfo : IRxnAppInfo
    {
        
        public string Name { get; }
        public string Version { get; set; }
        public string Url { get; }
        public string Id { get; }
        public bool KeepUpdated { get; }

        public AppVersionInfo(string name, string version, bool keepUptoDate)
        {
            KeepUpdated = keepUptoDate;
            Name = name;
            Version = version;
        }
    }

}
