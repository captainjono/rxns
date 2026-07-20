using System;

namespace Rxns.Interfaces
{
    public interface IDevice
    {
        string GetVersion();
        string GetOS();
        string GetConnection();
    }

    public class BasicDevice : IDevice
    {
        public string GetVersion()
        {
            return Environment.Version.ToString();
        }

        public string GetOS()
        {
            return Environment.OSVersion.Platform.ToString();
        }

        public string GetConnection()
        {
            return "unknown";
        }
    }
}
