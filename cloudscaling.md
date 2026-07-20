# Scaling

Reactions design philosophy includes the notion of seemlessly running on devices as well as in the cloud. Reactions not only makes your code portable, it makes it ***elasticially*** *scalable*. 

If the demands of your app overwhelm the device, at any time, you can choose to scale-out your app from in-process, to out of process, and then cloud, if yhour situation demands.

In other works, go from:
```
Single process App -> Multi-concurrent-process App -> Parts of App in cloud -> Cloud App
```

Without changing a line of your apps code.

How? with *scale points...*
```
App (scale point) -> 
OutOfProcess [specific reactors or multi-process entire App] (scalepoint)-> Cloud
```
>todo: make better diagram

Scaling points can be derrived from data in any event stream

# Cloud scaling
Since [reactors](reactors.md) run on events, they are suited to being backed by the highly the cloud, which seems to run on too. Multi-cloud scaling is support out of the box. ie. each reactor could be hosted in a different cloud. Or each service could be backed by a different clouds [BackingChannel](reactors.md)

The following cloud integrations are batteries included. In cases where you need to adapt to other clouds, you can extend reactions by implementing the corresponding interfaces yourself.

## Azure

Component | Usage | When
-|-|-
AzureServiceBusBackingChannel | RxnMamanger managed in the cloud for event streams which suite these semantics | Scaling out to multi-server achitecture
AzureFileSystemService | Host your Apps files in the cloud | Your App needs to leverage cloud storage
AzureFunctionRxnHost | Scale out reactors into the cloud| Scaling out specific reactors on demand in to miminise base-load costs or maximise scale potential
