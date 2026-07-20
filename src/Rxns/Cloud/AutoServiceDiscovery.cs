using System;
using System.Reactive.Linq;
using Rxns.Hosting;
using Rxns.Logging;

namespace Rxns.Cloud
{
    public class AutoServiceDiscovery : ReportsStatus, IAppServiceRegistry
    {
        private readonly IAppServiceDiscovery _appServices;
        public string AppStatusUrl { get; set; }

        public AutoServiceDiscovery(IAppServiceDiscovery appServices)
        {
            _appServices = appServices;

            _appServices.Discover()
            .Do(api =>
            {
                OnInformation($"Discovered {api.Name} @ {api.Url}");

                switch (api.Name.ToLower())
                {
                    case "appstatus":

                        AppStatusUrl = api.Url;
                        break;
                }
            })
            .Until(OnError).DisposedBy(this);
        }
    }
}
