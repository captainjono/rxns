<!-- TOC -->

- [1. Reactions](#1-reactions)
    - [1.1. Building blocks for *Reactive* Micro Apps](#11-building-blocks-for-reactive-micro-apps)
    - [1.2. Use-cases](#12-use-cases)
    - [1.3. Event Driven Primitives](#13-event-driven-primitives)
        - [1.3.1. Event Manager](#131-event-manager)
        - [1.3.2. Reactors](#132-reactors)
        - [1.3.3. Recording and playback](#133-recording-and-playback)
    - [1.4. Rx Primities](#14-rx-primities)
    - [1.5. Scheduler](#15-scheduler)
    - [1.6. Logging](#16-logging)
    - [1.7. Monitoring](#17-monitoring)
    - [1.8. Mediated App Pipelines](#18-mediated-app-pipelines)
    - [1.9. Cloud Resilliancy](#19-cloud-resilliancy)
    - [1.10. Hosting](#110-hosting)
        - [1.10.1. Supervised AppHost](#1101-supervised-apphost)
    - [1.11. A language](#111-a-language)
- [2. Addons](#2-addons)

<!-- /TOC -->

# 1. Reactions

Reactions makes building, maintaining and taming reactive apps a pleasure.
Concentrate on the functionality of your app, while knowing it can scaling horiztonally or vertically with ease.
Reactions are designed to grow with your app. Reactors move seemlessly from InProsss, to OutOfProcess, to Cloud ***zero-downtime*** in seconds 
Reactions are a way of seperating concerns in your app, de-coupling your buisness logic from your service logic from your scaling logic. The micro-arcitecture reimagines all facets of your app with a micro-services lense.
Reactions gives you insight into your app, exposing a rich set of a real-time diagnostics via its appstatus interface that can be access localy or remotely
Reactions is cloud native, without the cloud lockin. Migrate from in-process to Cloud based Queues, ServiceBuses, Functions with basic configuration changes.
Reactions support partially or fully event sourced architectures while providing deep insight into the event flow and bottlenecks in your sytsem.
Reactions belives some services shouldnt be outsourced and provides a base-layer of functionality critical to any reactive app. As your apps hockey-stick kicks in, you can replace any of these core functions with Cloud managed services with a few lines of code.
Reactions provides isolation and other semantics via its Reactors. (Re-Actors) can be thought of as Actors reimagined for the cloud.
Shields your app from cloud lockin

Highlights
* [Reactors](reactors.md)
* [DDD/CQRS](cloudpatterns.md)
* [RxnHosts](rxnhosts.md)
* [Cloud scaling](cloudscaling.md)
* [Docker support](rxncreate.md)
* [Real-time App Monitoring](scaling.md)
* [Task Scheduling](scheduler.md)
* [CI/DI](cicd.md)
* [Cloud Costings](costings.md) *comming soon*

## 1.1. Building blocks for *Reactive* Micro Apps

Reactions (*Rxns*) takes a pragmatic view of reactive app design centered around an event driven approach. See [reactors...](reactors.md)

Core motivations include:

* `IObservbale<T>` everything
* All features wrapped in domain interfaces
* Testability and maintainability are a first order concern, along with speed and ergonomics
* Sensible defaults that are resillisant to failures in occasionally connected environments
* Take it as you come. Each building block can be taken in isolation or as a whole *Module*.
  * Allows you dip your toes in the water before drinking the cool-aid and going all in.
* Without the cruft - *dependency free* on `.NET5`
    * Has been PCL complient since `2015`
    * Will always work on any platform that `.net` supports
      * `.NET5 .netCore / Full framework 4.0-4.8 / Mono / Xamarin / etc`
* Modern IDE friendly with an API that is discoverable via *intellisense*. Just start typing `IRxn..` or `Rxn..` and your on your way...

## 1.2. Use-cases
 
* Creating app pipelines that are decoupled and well structured
* Creating micro-servicess that are highly elastic
* Creating decoupled micro-frontends with `redux style`, `one-way`  dataflows
* Creating high performance event sourced apps that run on commodity infrastructure
* Useful for transitional architectures, adding event driven components to legacy systems in a peicemeal fashion
* Remove bottlenecks in existing apps without going all in on cloud native concepts
* Offloading tasks from expensive `monoliths` / `database centeric systems`
* Sheilding your your `domain modal` / `app` from cloud lock-in

<p>

<h1> The Building blocks </h1>

## 1.3. Event Driven Primitives

* Supports traditional event sourcing patterns
* Design now, scale later! Seperates the concern of reacting to an event, from event delivery, allowing your event driven components to evolve seperately from your transport / platform or hosting mechanism
* Each primivitate can be chained to an [EventManager] or [Reactor](#reactors) to partition your app features
* Types
  * `RxnPulsars` publish events on specific intervals
  * `RxnPublishers` publish events at any point in time based on there own internal semantics
  * `RxnProcessors` react to events with one or more events as a result
  * `RxnDectorators` event source the operations of *anything*
  * `RxnDictionary<T, V>` are a specialised use-case optimised for in-memory caches
  * `LazyCacheDecorator<T, TV>` takes it a step further, debouncing operations to the backing store in time-slipping buffers where last value wins and writes can be skipped
  * `ShardingQueueProcessingService`
    * Supports `duplexing` tenanted data over a single queue serviced by a configurable amount of workers
    * Allows you to *project* `events` into *stores* or *views* in a highly scalable way for highly scalable consumption

### 1.3.1. Event Manager

* Allows your event driven app to ***pub***lish and ***sub***scribe to events
* Allows you to worry about scaling later, and not be blocked by a design choice made early on
* Runs on a *backing channel* that allows you to evolve your eventbus capability seperatly to your domain logic
  * The `IBackingChannel` *is the transport mechansim*
  * `LocalbackingChannel` : events are routed in process only    
  * `AzureServiceBusBackingChannel` :events are routed over an a Azure Service Bus    
  * `LowestCostServiceBusBackingChannel` : events are routed over a cloud provider whos cost is lowest for the events you are sending *todo
  * `MessagaingCenterChannel` : send events over [Xamarins](http://xamarin.com/) building sub/sub system
  * `RoutablebackingChannel` : events are routed to different event managers depending on a condition
  * and more...
  
### 1.3.2. [Reactors](reactors.md)

* Event Managers and other [primitives](#event-driven-primitives) can be `chained` to reactors to, communicating in an isolated way from other app components.
* Couples components together to create resiliant, isolated, self-managed, sub-systems
* A scale out vector, as the `Reactor` grows, move it out of process without modifying a single line of code
* A reactors lifecycle is independant of your apps lifecycle, so they can be stopped or started without your app going down
* Mimics the supervisor pattern used in [Erlang](erlang.org)
* Automatically setups up metrics channel that provides deep insights into the event-flow of your system including the `back-pressure` of your reactions
  * Critical to understanding why your app is [event storming]()
  * Can be remotely viewed in real-time via the [AppStatus](#hosting) portal

### 1.3.3. Recording and playback

* Great tools to make your app more battle hardended
* Unlock advanced event sourcing features such as `undo`, `redo`
* Unit/Integration testing via tape playback
  * Automate integration test generation by recording user actions and storing in ITapeRepository for playback by the `UserAutomationPlayer`
*  Can be used to level up error reporting and diagnosis and produce piplines that empower devs with full-repo steps

The gist is...
* `EventTapes` store events in chronolical order via a `ITapeRecorder` 
* `ITapeArrays` represent a series of tapes and can be combined with a `ITapeToTenantRepoPlaybackAutomator` to create tenanted recording and playback pipelines
  
## 1.4. Rx Primities

* Rxns attempts to simplify the cognatic load and resulting code when utilising Rx with *vigour* throughout your app
* Its not just sugar though, the primiviates come default with error handling to help reduce maintainece overhead
* Api is designed with intellisense in mind
  * Start typing `Rxn..` and you will discover the api naturally
  * `Rxn.Create` is your friend, use it whenever you want to `Create` a `reaction` out of `something`. 
      * From an action `Rxn.Create(Action) : IObservable<Unit>;`
    * From a function result `Rxn.Create(Func<T>) :     IObservable<T>;`  
    * From another reaction `Rxn.Create(Rxn.In(Timespan.FromSeconds(2)))`

## 1.5. [Scheduler](scheduler.md)
* `RxScheduler` implemented behind `IRxScedhuler`
* highly configurable, tasks can come from one or more sources
  * `ITaskProvider` with a default implementations for exposing tasks via a `tasks.json` file or `inline`      
* Tasks are hot-reloaded on configuraiton updates
* Batteries included binding system that allows any properties on a task to be dervied from dynamic values in your system
* High resolution - schedule tasks on in `ms`'s or `years`
  * Supports cron schedulers with addon
* Resillisant - no time slippage. No matter when your tasks finish, it will maintain a consistant schedule
* Tasks can can be grouped and combined to create workflows that can use common logic operators to model complex buisness logic. 
* Runs on pure `Rx` so its *fast* & *portable* with your app

## 1.6. Logging

* Basic logging, simplified `Logger.WriteToDebug()` then later`"HellowWorld".LogInfo()` `.LogDebug()` `.LogError(ex)`
* Apps which use logging to alter system state can use a formalised approach of `IReportsStatus` and create `Rxns` out of their log streams with methods `OnInformation()` `OnWarning` `OnError`

## 1.7. [Monitoring](#scaling)

* Monitor your app remotely via a centralised `AppStatus` portal
* Supports any of the following
  * `Heartbeating` provides real-time metrics on your app on predefined intervals. Submit any metric you want with your heartbeat and it will be displayed in the portal
  * `Remote Commanding` control your app remotely, sending it commands like `UploadLogs` `Restart` and `Update`  
  * `Error Reporting` your app can be configured with various `IErrorChannels` and the `AppStatusErrorChannel` will surface the stacks and a dump of log messages around the time of the error for your to later catalog and browse
  * `App Updates` upload versions of your app to the portal then by utilising the `UpdatesModule` to your app you can seemlessly have it update on `demand` or `automatically` whenever versions are published
  * `Monitor Event`
  * `Real-time logging` connect to the appstatus portal and stream your apps logs in real-time or historically
  * `Log uploads` sometimes your apps logs might not stream that well, in those cases you can instruct your app to upload its logs *(as a zip)* and for your analyis.

## 1.8. Mediated App Pipelines
* Create CQRS / DDD style systems that react to `IDomainCommand<T>` or `IDomainQuery<T>`
* Handle them with `IDomainCommandHandler<T>` 
* `ICommandService` allows you to call these APi functions in same way from the client or the server
* These pipelines that are mediated by `IDomainCommandMediator<T>` can be augmented with `IDomainCommandPreHandler<T>` or `IDomainCommandPostHandler[]<T, TR>` request handlers to perform transformations, validation and more

## 1.9. Cloud Resilliancy

* Exposes a `IReliabilityManager` that is based on [Polly](http://www.thepollyproject.org/)
* Makes your app predictable in unpredictable environments
* Supports common cloud patterns
  * Circut breaker
  * Retry policies based on error messages  
    * ie. Use `progressive backoffs, that settle into 30minute indefinate retries` for some error cases, or `3 retries at most` for others
  * Define retry semantics that are decoupled from domain logic
* Scalout reactors seemlessly from inprocess <-> out of process <-> into the cloud

## 1.10. Hosting

* Reactions can be hosted anywhere `.net code` runs. Integrate it into your existing app or favourate framework using `Rxn.Create()` or..
* Reactions also run on *AppHosts* which are basic abstractions used to decouple your app from its operating system
* Batteries included with:
  * `ConsoleHost` exposes your reaction via the `.NET` console app
  * `ServiceHost` exposes your reaction via `.NET` service
  * `WebApiHost` or `KrestalHost` exposes your reaction via a `.NET4.8 Owin WebApi` or `.NET5 Krestal` web server
    * Can be combined with an [AppStatus Portal](#monitoring) to create a monitoring agent
  * Simple to extend and create your hosts

### 1.10.1. [Supervised AppHost](rxnhosts.md)
  * JSON configurable to run any `.NET` `.dll`
    * Perfect for [Docker](http://docker.org/) deployments
  * Will launch your app *in a seperate process* and monitor it
  * Will automatically restart your app on failure
    * Supports the "let it crash" philisphohy of Erkabg
    * Logs app output
  * Allows your app to hot-update via an `IUpdateService`
      * Which can be hosted easily in an `AppStatus` portal
      * ...or any other Rxn!
* Soon
  * Clustering support, run multiple processes chains to a single AppHost
  * Cluster around an `IEventManager` which will round-robin deliver messages to support out-of-process workers
  * Define elastisiticy of cluster to `auto-spawn` processes based on condiditional logic
  * Can be used to create reactive-auto-scaling micro-services that are scale based on system resource consumption or other metrics
  
## 1.11. [A language](#rxncreate)

* Rxns can also be taken as a language. "I react to something" "This reaction performs the notifications"
* The synatax attempts to be fluent and create living documentation

# 2. Addons

* While Rxns is dependecy free, but it works great with
  * *Autofac* `new Container().ToRxns()` will turn any container into a RxnApp
  * `AutofacModule`