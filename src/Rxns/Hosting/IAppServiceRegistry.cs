using System;
using System.Reactive;

namespace Rxns.Hosting
{
    public interface IAppServiceRegistry
    {
        string AppStatusUrl { get; set;}
    }

    public interface IAppServiceDiscovery
    {
        IObservable<ApiHeartbeat> Discover();
        IObservable<Unit> Advertise(string system, string apiName, string apiUrl);
    }
    
    public class ApiHeartbeat
    {
        public string Name { get; set; }
        public string Url { get; set; }

    }

    public class AppServiceRegistry : IAppServiceRegistry
    {
        public string AppStatusUrl { get; set; }
    }
}
