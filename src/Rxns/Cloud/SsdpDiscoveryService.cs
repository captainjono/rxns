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
    public class SsdpDiscoveryService : IAppServiceDiscovery, IDisposable
    {
        private static readonly object _gate = new object();
        private static SsdpCommunicationsServer _server;
        private static int _refCount;

        private static SsdpCommunicationsServer Acquire()
        {
            lock (_gate)
            {
                if (_server == null)
                {
                    var localIpAddress = RxnApp.GetIpAddress();
                    $"Found best local ip address: {localIpAddress ?? "(none — using Any)"}".LogDebug();
                    _server = new SsdpCommunicationsServer(new SocketFactory(localIpAddress));
                    _server.IsShared = true;
                }
                _refCount++;
                return _server;
            }
        }

        private static void Release()
        {
            lock (_gate)
            {
                if (--_refCount <= 0 && _server != null)
                {
                    try { _server.Dispose(); } catch { }
                    _server = null;
                    _refCount = 0;
                }
            }
        }

        public IObservable<ApiHeartbeat> Discover()
        {
            return Rxn.Create<ApiHeartbeat>(o =>
            {
                var server = Acquire();
                var locator = new SsdpDeviceLocator(server);

                var watcher = Observable.FromEventPattern<DeviceAvailableEventArgs>(e => locator.DeviceAvailable += e,
                        e => locator.DeviceAvailable -= e)
                    .Where(d => (d.EventArgs.DiscoveredDevice?.Usn ?? "").IndexOf("support-portal", StringComparison.OrdinalIgnoreCase) >= 0)
                    .Do(args =>
                    {
                        var d = args.EventArgs.DiscoveredDevice;
                        string friendly = null;
                        var usn = d.Usn ?? "";
                        var urnIdx = usn.IndexOf("urn:", StringComparison.OrdinalIgnoreCase);
                        if (urnIdx >= 0)
                        {
                            var parts = usn.Substring(urnIdx).Split(':');
                            if (parts.Length >= 6) friendly = parts[parts.Length - 2];
                        }
                        o.OnNext(new ApiHeartbeat()
                        {
                            Name = d.Usn,
                            Url = d.DescriptionLocation.ToString(),
                            FriendlyName = friendly
                        });
                    })
                    .Until();

                locator.StartListeningForNotifications();
                locator.SearchAsync("rxns");

                return Disposable.Create(() =>
                {
                    "Stopping SSDP discovery".LogDebug();
                    try { locator.Dispose(); } catch { }
                    try { watcher.Dispose(); } catch { }
                    Release();
                });
            });
        }

        public IObservable<Unit> Advertise(string system, string apiName, string apiUrl, string friendlyName = null)
        {
            return Rxn.Create<Unit>(o =>
            {
                var server = Acquire();

                $"{apiUrl}".LogDebug("Advertising");

                var emitter = new SsdpDevicePublisher(server);
                emitter.NotificationBroadcastInterval = TimeSpan.FromSeconds(1);
                emitter.AddDevice(new SsdpRootDevice()
                {
                    DeviceTypeNamespace = system,
                    FriendlyName = friendlyName ?? apiName,
                    Location = new Uri(apiUrl),
                    Manufacturer = "rxns",
                    ModelName = apiName,
                    DeviceType = $"{apiName}:{friendlyName ?? apiName}",
                    Uuid = $"uuid:{Guid.NewGuid().ToString()}"
                });

                return Disposable.Create(() =>
                {
                    $"Stopping advertising of {apiName}".LogDebug();
                    try { emitter.Dispose(); } catch { }
                    Release();
                });
            });
        }

        public void Dispose()
        {
        }
    }
}
