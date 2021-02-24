using System;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Rxns.Hosting;
using Rxns.Logging;
using Rxns.Rssdp;
using Rxns.Rssdp.Infrastructure;

namespace Rxns.Cloud
{
    public class SsdpDiscoveryService : IAppServiceDiscovery
    {
        public IObservable<ApiHeartbeat> Discover()
        {
            var watcher = Disposable.Empty;
            SsdpDeviceLocator locator = null;

            return Rxn.Create<ApiHeartbeat>(o =>
            {
                var localIpAddress = RxnApp.GetIpAddress();
                SsdpCommunicationsServer server = null;

                if (localIpAddress != null)
                    $"Found best local ip address: {localIpAddress}".LogDebug();


                if (localIpAddress.IsNullOrWhitespace())
                {
                    server = new SsdpCommunicationsServer(new SocketFactory(localIpAddress));
                    locator = new SsdpDeviceLocator(server);
                }
                else
                {
                    locator = new SsdpDeviceLocator();
                }

                watcher = Observable.FromEventPattern<DeviceAvailableEventArgs>(e => locator.DeviceAvailable += e,
                        e => locator.DeviceAvailable -= e)
                    .Where(d => d.EventArgs.IsNewlyDiscovered)
                    .Do(args =>
                    {
                        if (args.EventArgs.IsNewlyDiscovered)
                            o.OnNext(new ApiHeartbeat()
                            {
                                Name = args.EventArgs.DiscoveredDevice.Usn,
                                Url = args.EventArgs.DiscoveredDevice.DescriptionLocation.ToString()
                            });
                    })
                    .Until();

                locator.StartListeningForNotifications();
                locator.SearchAsync("rxns");

                return Disposable.Create(() =>
                {
                    "Stopping SSDP discovery".LogDebug();

                    server?.Dispose();
                    locator.Dispose();
                    watcher.Dispose();
                });
            });

        }

        public IObservable<Unit> Advertise(string system, string apiName, string apiUrl)
        {
            return Rxn.Create<Unit>(o =>
            {
                var localIpAddress = RxnApp.GetIpAddress();
                SsdpCommunicationsServer server = null;
                SsdpDevicePublisher emitter = null;

                $"http://{localIpAddress}:896/".LogDebug("Advertising");

                server = new SsdpCommunicationsServer(new SocketFactory(localIpAddress));
                emitter = new SsdpDevicePublisher();
                emitter.NotificationBroadcastInterval = TimeSpan.FromSeconds(1);
                emitter.AddDevice(new SsdpRootDevice()
                {
                    DeviceTypeNamespace = system,
                    FriendlyName = apiName, 
                    Location = new Uri(apiUrl), 
                    Manufacturer = "rxns", 
                    ModelName = apiName, 
                    DeviceType = apiName,
                    Uuid = $"uuid:{Guid.NewGuid().ToString()}"
                });

                return Disposable.Create(() =>
                {
                    $"Stopping advertising of {apiName}".LogDebug();
                    server?.Dispose();
                    emitter?.Dispose();
                });
            });
        }
    }
}
