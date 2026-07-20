using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns.Hosting.Cluster
{
    public class ClusteringWorkflow
    {
        public Dictionary<string, IRxnAppContext> Apps = new Dictionary<string, IRxnAppContext>();
        private readonly IRxnAppProcessFactory _factory;
        private readonly IRxnAppScalingManager _scalingManager;
        private readonly IRxnAppCfg _cfg;
        private readonly IRxnManager<IRxn> _rxnManager;

        public ClusteringWorkflow(IRxnAppProcessFactory factory, IRxnHostManager hostManager, IRxnAppScalingManager scalingManager, IRxnAppCfg cfg, IRxnManager<IRxn> rxnManager)
        {
            _factory = factory;
            _scalingManager = scalingManager;
            _cfg = cfg;
            _rxnManager = rxnManager;
            Cluster = new RxnAppClusterManager();
            _hostManager = hostManager;
        }

        private readonly IRxnHostManager _hostManager;

        public RxnAppClusterManager Cluster { get; set; }

        public RxnMode DetectMode(IRxnAppCfg argsOriginal)
        {
            return _factory.DetectMode(argsOriginal);
        }

        public IObservable<IRxnAppContext> Run(IRxnHostableApp app, IRxnAppCfg cfg)
        {
            var mode = _factory.DetectMode(cfg);
            
            return _factory.Create(app, _hostManager, cfg.Args.AnyItems() ? cfg.Args : null, mode)
                .SelectMany(rxnApp =>
                {
                    "App starting".LogDebug();
                    Apps.Add("supervisor", rxnApp);

                    _scalingManager.Manage(rxnApp);

                    return rxnApp.Start(false);
                })
                .SelectMany(a =>
                {
                    return a.Status.Where(s => s == ProcessStatus.Active).Select(_ => a);
                });
        }
        public IObservable<IRxnAppContext> SpawnReactor(IRxnHostableApp app, string name, Type[] routes)
        {
            if (_cfg.Args.Contains("reactor"))
            {
                return _rxnManager.Publish(new SendReactorOutOfProcess(name, routes)).Select(_ =>
                {
                    var rxnApp = new RxnManagerClusterClient(_rxnManager, new[] {"reactor", name});

                    Apps.Add(name, rxnApp);

                    return rxnApp;
                });
            }

            if (Apps.ContainsKey(name))
            {
                $"Bypassing because {name} already spawn".LogDebug();
                return Apps[name].ToObservable();
            }

            return _factory.Create(app, _hostManager, name, RxnMode.OutOfProcess)
                .SelectMany(rxnApp =>
                {
                    $"{name} starting".LogDebug();

                    Apps.Add(name, rxnApp);

                    _scalingManager.Manage(rxnApp);

                    return rxnApp.Start(true);
                })
                .Select(isolatedReactor =>
                {
                    "Reactor spawned".LogDebug();

                    return isolatedReactor;
                });
        }

        public void Dispose()
        {
            Apps.Values.ForEach(a => a.Dispose());
            Apps.Clear();
        }
    }
}
