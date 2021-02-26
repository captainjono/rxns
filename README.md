<!-- TOC -->

1. [1. Reactions](#1-reactions)

<!-- /TOC -->

# 1. Reactions

*What?*
- *Multi*-Cloud Native
- Elastically scalable
- Reactive building blocks
- For Reliable & Maintainable
- Self-documenting, Cross-platform
- <h3>Micro <i>Apps</i>!</h3>

*And that means*?

Reactions makes building, maintaining and taming Reactive Apps a pleasure.

*How?*

Use the familiar Rx API to create reactive micro apps,  that can scale horiztonally or vertically by implementing the Reactions building blocks & patterns.

*In Short!*

- Reactions are designed to grow with your app. 
  [Reactors](reactors.md) move seemlessly from [0InProsss, to OutOfProcess, to Cloud](cloudscaling.md) with ***zero-downtime*** in seconds 

- Reactions are a way of [seperating concerns in your app](strangleAMonolith.md), de-coupling your buisness logic from your service logic from your scaling logic. The micro-arcitecture reimagines all facets of your app with a micro-service lense.
- Reactions gives you [insight into your app](appstatus.md), exposing a rich set of a real-time diagnostics via its appstatus interface that can be accessed localy or remotely
- Reactions is [cloud native](CloudPatterns.md), without the cloud lock-in. Migrate from in-process to Cloud based Queues, ServiceBuses, Functions with basic code changes.
- Reactions support partially or fully event sourced architectures while providing deep insight into the event flow and bottlenecks in your App.

Core motivations include:

* `IObservbale<T>` everything
* All features wrapped in domain interfaces
* Testability and maintainability are a first order concern, along with speed and ergonomics
* Sensible defaults that are resillisant to failures in occasionally connected environments
* Take it as you come. Each building block can be taken in isolation or as a whole *Module*.
  * Allows you dip your toes in the water before drinking the cool-aid and going all in.
* Without the cruft - *dependency free* on `.NET5`
    * Has been PCL complient since `2013`
    * Will always work on any platform that `.net` supports
      * `.NET5+ .netCore / Full framework 4.6.1+ / Mono / Xamarin / etc`
* Modern IDE friendly with an API that is discoverable via *intellisense*. Just start typing `IRxn..` or `Rxn..` and your on your way...


*....Philisophy?* 
- Reactions belives some services shouldnt be outsourced and provides a base-layer of functionality critical to any reactive app. Made up of intelligently choosen patterns and practices that dont lock you in...  The idea being, as your apps hockey-stick kicks' in, you can replace any of these core functions with Cloud managed services in a cost-effective, piecemeal way.

*What capabilities does Reactions provide?*

* [Reactors](reactors.md)
* [AppStatus](appstatus.md)
* [DDD/CQRS](cloudpatterns.md)
* [DDD Aggegrates](dddaggs.md)
* [Domain Contexts](dddcontexts.md)
* [BuildingBlocks](buildingblocks.md)
* [RxnHosts](rxnhosts.md)
* [Cloud scaling](cloudscaling.md)
* [Docker support](rxncreate.md)
* [Real-time App Monitoring](scaling.md)
* [Task Scheduling](scheduler.md)
* [CI/DI](cicd.md) 
* [Migration guide / strangle the monolith](strangleAMonolith.md)
* [ViewProcessors](ViewProcessors.md)
* [Azure Integration](rxnInAzure.md)
* [Cloud Costings](costings.md) *comming soon*

*Where or How should I apply Reactions to my code?*
 
* Creating app pipelines that are decoupled and well structured
* Creating micro-services that are [highly elastic](cloudscaling.md)
* Creating decoupled micro-frontends with `redux style`, `one-way`  data->flows
* Creating high performance event sourced apps that run on commodity infrastructure
* Useful for [transitional architectures](strangleAMonolith.md), adding event driven components to legacy systems in a peice-meal fashion
* Remove bottlenecks in existing apps without going all in on cloud native concepts
* Offloading tasks from expensive `monoliths` / `database centeric systems`
* Sheilding your your `domain modal` / `app` from cloud lock-in

<p>

