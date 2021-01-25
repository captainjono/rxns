# Continious Deployment

Reactions are highly deployable my nature. They were designed to be worm-like in everything but usage.

# How?

Use [`Rxn.create`](rxncreate.md)

1. In your build pipeline, once finished, deploy your built app to an [AppStatus](scaling.md) portal
```
NewAppUpdate {name} {version} {applocation} {isLocal} {appStatusUrl}
```  

You now have an update published. This update can act on a ScalePlan

2. Now on a bare metal host, VM, [Docker](docker.com) or on *mobile device*, run
```
rxn.create FromAppUpdate {name} {version} {binary} {applocation} {isLocal} {appStatusUrl}
```

and it will *be ! ! !*

or

Configure your AppStatus portal with a Scale out plan

```
AutoBalancingHostManager
    .ConfigureWith(new AutoScaleoutReactorPlan(
        new ScaleoutToEverySpareReactor(), 
        {name}, 
        {reactor||null}, 
        {version||latest}
        )
    ))
```

then spinup a SpareReactor on your host instead

`rxn.create SpareReactor {appStatusUrl}`

Thats the basics of acheving solid CD with reactions.  

## Advanced

* Gated checkin or other source controled related features are out of scope for reactions.
* Supports rolling app updates, with auto-rollback  
  * On update, the installer is run
* Your app can run an installer
  
## App Updates

You App can be kept automaticaly updated in the wild with an AppStatus portal. 
* Configure your App `IRxnAppCfg` with a `KeepUpToDate:true` & `Version:Latest` and the next time you deploy an App Update, any existing systems will download, install and reboot at the next verision, automatically.

* Use can preconfigure reactions with a `rxn.cfg`
* You can post-fix `Latest` with `Latest-1.0` and on upload you app will get an automatic version number based on the time and date ie. `{appName}-1.0.2021-01-23T152013`

