# Clustering

Using this host you *seriously* turn up the throughput that your App can sustain using a one or more of the scaling patterns that the host faciliatates.

```c#
 public ClusteredAppHost(
     IRxnAppProcessFactory processFactory, // used for out of process scaleout
     IRxnAppProcessFactory cloudFactory, // used for cloud scaleout
     IRxnManager<IRxn> hostEventLoop, // used communicate with each process
     RoutableBackingChannel<IRxn> router, // the router used multiple communication to all the processes
     IRxnHostManager hostManager, // allows the host to increate closely with the headerware
     IRxnAppCfg cfg, //configures the host
     IStoreAppUpdates appStore // can scaleout *ANY* reaction in your existing AppUpdate library //see: #ApptSatus
 )
```

The main objective of this hots is to provide an abstraction that will do all the heavy lifting in your Apps architecture in terms of utilising multiple cores on your host.

```c#
  return OutOfProcessDemo.DemoApp(RxnApp.SpareReator(url))
                    .ToRxns()
                    .Named(new ClusteredAppInfo("DemoApp", cfg.Version, args, false))
                    .OnHost(new ClusteredAppHost(
                            new OutOfProcessFactory(appStore),
                            OutOfProcessFactory.RxnManager,
                            OutOfProcessFactory.PipeServer?.Router,
                            new AutoBalancingHostManager().ConfigureWith(new ConsoleHostedApp(), _ => true), cfg, appStore)
                                .ConfigureWith(new AutoScalingAppManager()
                                .ConfigureWith(new ReliableAppThatRestartsOnCrash(OutOfProcessFactory.RxnManager))
                                .ConfigureWith(new AutoScaleoutReactorPlan(new ScaleoutToEverySpareReactor(), "DemoApp", "Cache", "1.0"))),
                            cfg)
                    .SelectMany(h => h.Run())
                    .Do(app =>
                    {
                        $"Advertising to {url}".LogDebug();
                        app.RxnManager.Publish(new PerformAPing()).Until(ReportStatus.Log.OnError);
                    })
                    .Subscribe(o);
```

The clusterhost itself is fairly lightweight and delegates responsiblity to how it operates to support classes

Component | Description
-|-
`AutoBalancingManager` | Will balance your cluster by reacting to changes in [RxnHealth](#rxn-health)
`ReliableAppThatRestartsOnCrash` | This component will monitor your App for crashes and restart it whenever that happens, *optionally* reporting the occourance to [AppStatus](appstatus.md)
`AutoScaleoutReactorPlan` | Any App which which connects to this cluster with the name "`SpareReactor`" will be `automoatically` instructed to *update itself* with the App listed, restarting itself and performing any [installation](crossplatform_installer.md) *inbetween*

The api of the cluster host is centered around spawning processes. Everything else it is configured via its options.

API | Description

`Spawn` | Spawns a new process

## Configuration

You can configure a ClusterHost via your `rxn.cfg` to start certain Apps everytime the cluster starts. This way you can create complex networks of micro-service dependencies and they will *all* be `spawned` on startup.

```json
{   
    "apps": [
        {
            "name": "DomainAPI Response Cache",
            "path": "../packages/Redis/redis.exe",        
        },
        {
            "name": "DemoApp",
            "path": "DemoApp.dll",        
        },
    ]
    "appStatusUrl": "auto | http://localhhost:888"
}
```

## Patterns & Practices

Each pattern can be composed together to create complex workflows that will push the limits of your devices `compute` power

Pattern | Description | Application
-|-
Workers | Automatically `spawns` *multple* **instances** of your App and **routes** work to each instance using a *configurable* algorith | Queues, Multi-tenancy isolation
Multi-Process | `spawns` each reactor into own processes and only routes events to that reactor if it has a route registered to it for that event | Multi-tenancy
Supervisor | `spawn` any

