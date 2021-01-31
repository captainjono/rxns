# Continious Deployment

Moving from one machine to another is basic Cloud Native App pattern. Reactions take this to heart and are consequently *highly deployable by nature*. In essenance, were designed to be *worm-like* in everything but **usage ;)**
<!-- TOC -->

1. [Continious Deployment](#continious-deployment)
2. [How?](#how)
3. [Deployment Basics](#deployment-basics)
   1. [Keeping up to date](#keeping-up-to-date)
4. [Scaling guide](#scaling-guide)
5. [Advanced Scenarios](#advanced-scenarios)

<!-- /TOC -->
# How?

Builds of your app can be deployed to an `AppUpdateServer`. From there, they can be used in a variety of ways:
1. To provide a REST Api to download versions of your app.
2. To act as a server that automatically keeps your deployed Apps *up-to-date*.
3. Create *elastic clusters* of resources that balance in real-time based on your Apps *load*.

These techniques are discussed in the deployment guide below.

>More info: [*about* AppStatus](scaling.md)

# Deployment Basics
Use [`Rxn.create`](rxncreate.md)

1. In your build pipeline, once finished, deploy your built app to an [AppStatus](scaling.md) portal

```
rxn.create NewAppUpdate {name} {version} {applocation} {isLocal} {appStatusUrl}
```  

and your app is now availible to download from

`http(s): //{portalUrl}/updates/{application}/{version}`

2. Now on a bare metal host, VM, [Docker](docker.com) or on *mobile device*, run
```
rxn.create FromAppUpdate {name} {version} {binary} {applocation} {isLocal} {appStatusUrl}
```

Your app is now deployed and running on the target machine.

## Keeping up to date

To support `AppUpdates`, first you need too:

1. [Include the](appcontainer.md) following components in your App definition
   
   * `def`
    ```c#
        def
          //sets up your reaction report to AppStatus
          .Include<AppStatusClientModule>()
          .CreatesOncePerApp(_ => new AppServiceRegistry() {
            //the url of AppStatus / update server
            AppStatusUrl = {AppStatusUrl}
          })
    ```

You can *then* keep your app up to date manually by

2. Sending it an update command:
`UpdateSystemCommand {application} {version} {route}`

> What is yours Apps route? see [App routing](#rxnmanager)

or if you want to make it update automatically on deployment

3. Update your [rxndef](#appcontainer.md) with:   
   ```c#
   
          //how often you want to check for updates
          def
            .CreatesOncePerApp(_ => new ReliableAppThatHeartbeatsEvery(TimeSpan.FromSeconds(10)))
            .CreatesOncePerapp(_ => new RxnAppInfo({application}, "Latest", keepUpToDate: true))
   ```
   

and then your app will heartbeat every `10 minutes`, at which time it will *automatically* update if new versions are availible

>NOTE:   Reactions supports more advanced deployment scenarios including *auto-rollback* to previous versions when *deployments fail*.
>More Info: [see Realiable Rxns](rxninstall.md)

# Scaling guide

Your app updates can double as mechanism to scale your infrastructure to hockey-stick like uptake. 

1. To configure your [AppStatus](appstatus.md) portal with a Scale out plan:

```c#
AutoBalancingHostManager
    .ConfigureWith(new AutoScaleoutReactorPlan(new ScaleoutToEverySpareReactor(), 
                                              {application}, 
                                              {reactor || null}, //scale out a specific reactor of this app only(!!!)
                                              {version || "Latest"}
                                              )
    ));
```

>Note: You can choose to only scaleout specific [reactors](reactors.md) inside of your Rxns!

2. then spinup a [SpareReactor](sparereactor.md) pointing at your host

`rxn.create SpareReactor {appStatusUrl}`

It will connect and the AppStatus portal will update it with your scaleout plan.

Thats the basics of acheving solid CD with reactions.  

# Advanced Scenarios

* Gated checkin or other source controled related features are out of scope for reactions.
* Supports rolling app updates, with auto-rollback  
  * On update, the installer is run
  * Your app can run an installer
* You can preconfigure reactions with a `rxn.cfg`
* You can post-fix `Latest` with `Latest-1.0` and on upload you app will get an automatic version number based on the time and date ie. `{appName}-1.0.2021-01-23T152013`

