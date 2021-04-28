using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Rxns.Health.AppStatus;
using Rxns.Interfaces;
using Rxns.Logging;
using Rxns.Metrics;
using Rxns.Microservices;

namespace Rxns.Hosting
{
    public class DynamicStartupTask : IContainerPostBuildService
    {
        private Action<IReportStatus, IResolveTypes> _toRun;

        public DynamicStartupTask(Action<IReportStatus, IResolveTypes> toRunOnStartup)
        {
            _toRun = toRunOnStartup;
        }

        public void Run(IReportStatus logger, IResolveTypes container)
        {
            _toRun(logger, container);
        }
    }

    public class RxnApp : IRxnApp, IAppEvents
    {
        private readonly IObservable<IDisposable> _inline;
        private readonly IRxnAppFactory _rxnFactory;

        public IRxnDef Definition { get; }
        public IAppSetup Installer => new NoInstaller();


        public RxnApp(Type app, IRxnDef def, IRxnAppFactory rxnFactory)
        {
            //_app = def.Container.Resolve(app) as IMicroApp;
            Definition = def;
            _rxnFactory = rxnFactory;
        }

        public RxnApp(IObservable<IDisposable> inline, IRxnDef def, IRxnAppFactory rxnFactory)
        {
            _inline = inline;
            Definition = def;
            _rxnFactory = rxnFactory;
            
            Definition.UpdateWith(l =>
            {
                l.CreatesOncePerApp(_ => this);
                l.CreatesOncePerApp(_ => new DynamicStartupTask((___, __) => inline.Until()));
            });
        }

        public RxnApp(IRxnDef def, IRxnAppFactory rxnFactory)
        {
            Definition = def;

            _rxnFactory = rxnFactory;
        }

        public IObservable<IRxnAppContext> Start(bool startRxns = true, IAppContainer container = null)
        {
            var finalContainer = container ?? Definition.Container;
            var app = _rxnFactory.Create(this, finalContainer, finalContainer, RxnSchedulers.TaskPool);
            
            return app.Start(startRxns, container).Do(appContext=>
            {
                if (startRxns)
                {
                    var preStartupServices = appContext.Resolver.Resolve<IContainerPostBuildService[]>();
                    preStartupServices.ForEach(s =>
                    {
                        try
                        {
                            s.Run(finalContainer, app.Resolver);
                        }
                        catch (Exception e)
                        {
                            container.OnError(e);
                        }
                    });
                }

                _onStart.OnNext(new Unit());
            });
        }

        /// <summary>
        /// Avoid VM / VPN / local adapters to listen on
        /// </summary>
        /// <returns></returns>
        public static string GetIpAddress()
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

                    }

                    return address.Address.ToString();
                }
            }

            return mostSuitableIp?.Address.ToString();
        }

        public static Func<string, Action<IRxnLifecycle>> SpareReator = appStatusUrl => spareReactor =>
        {
            appStatusUrl = appStatusUrl ?? "http://localhost:888";

            spareReactor
                .Includes<AppStatusClientModule>()
                .Includes<RxnsModule>()
                .CreatesOncePerApp<NoOpSystemResourceService>()
                .CreatesOncePerApp(_ => new ReliableAppThatHeartbeatsEvery(TimeSpan.FromSeconds(10)))
                .CreatesOncePerApp<INSECURE_SERVICE_DEBUG_ONLY_MODE>()
                .CreatesOncePerApp(() => new AggViewCfg()
                {
                    ReportDir = "reports"
                })
                .CreatesOncePerApp(() => new AppServiceRegistry()
                {
                    AppStatusUrl = appStatusUrl
                })
                .CreatesOncePerApp<UseDeserialiseCodec>();
        };


        private readonly ReplaySubject<Unit> _onStart = new ReplaySubject<Unit>();
        public IObservable<Unit> OnStart => _onStart.ObserveOn(CurrentThreadScheduler.Instance).SubscribeOn(CurrentThreadScheduler.Instance);

        public void Dispose()
        {
            _onStart?.Dispose();
        }
    }

    public class NoInstaller : IAppSetup
    {
        public void Dispose()
        {
            
        }

        public TimeSpan Timeout { get; set; }
        public void Install()
        {
            "App does not have an installer".LogDebug();
        }

        public void Uninstall()
        {
            "App does not have an uninstaller".LogDebug();

        }
    }
}
