using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
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
                var localIpAddress = GetLocalIpAddress();
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
                var localIpAddress = GetLocalIpAddress();
                SsdpCommunicationsServer server = null;
                SsdpDevicePublisher emitter = null;

                var url = $"http://{GetLocalIpAddress()}/".LogDebug("BROADCAST");

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

        /// <summary>
        /// In a system with multiple network adapters, sometimes an 'incorrect' adapter is bound instead of the one you'd
        /// expect. For example, rather than using your WiFi network, the system might pick a VPN adapter etc. In this case
        /// packets are sent/received on the network and you often get no responses, or unexpected/incorrect responses. To
        /// solve this you can manually specify an IP by passing it to the constructor of a SsdpCommunicationsServer instance,
        /// then passing that instance to the publisher/locator instance. 
        /// 
        /// https://github.com/Yortw/RSSDP/wiki/Frequently-Asked-Questions#i-dont-get-search-results-or-notifications
        /// </summary>
        /// <returns></returns>
        public static string GetLocalIpAddress()
        {
            UnicastIPAddressInformation mostSuitableIp = null;
            var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();

            foreach (var network in networkInterfaces)
            {
                if (network.OperationalStatus != OperationalStatus.Up)
                    continue;

                var properties = network.GetIPProperties();

                if (properties.GatewayAddresses.Count == 0)
                    continue;

                foreach (var address in properties.UnicastAddresses)
                {
                    if (address.Address.AddressFamily != AddressFamily.InterNetwork)
                        continue;

                    if (IPAddress.IsLoopback(address.Address))
                        continue;

                    
                    try
                    {
                        if (!address.IsDnsEligible)
                        {
                            if (mostSuitableIp == null)
                                mostSuitableIp = address;
                            continue;
                        }

                        if (address.PrefixOrigin != PrefixOrigin.Dhcp)
                        {
                            if (mostSuitableIp == null || !mostSuitableIp.IsDnsEligible)
                                mostSuitableIp = address;
                            continue;
                        }
                    }
                    catch (Exception)
                    {
                        $"WARNING: could not up prefixorigin of {address.Address}. Ignoring.".LogDebug();
                        //mostSuitableIp = address;
                    }

                    return address.Address.ToString();
                }
            }

            return mostSuitableIp?.Address.ToString();
        }
    }
}
