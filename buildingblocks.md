# Building Blocks

## 1.3. Event Driven Primitives

* Supports traditional event sourcing patterns
* Design now, scale later! Seperates the concern of reacting to an event, from event delivery, allowing your event driven components to evolve seperately from your transport / platform or hosting mechanism
* Each primivitate can be chained to an [EventManager] or [Reactor](#reactors) to partition your app features
* Primitives include:
  * `RxnPulsars` publish events on specific intervals
  * `RxnPublishers` publish events at any point in time based on there own internal semantics
  * `RxnProcessors` react to events with one or more events as a result
  * `RxnDectorators` event source the operations of *anything*
  * `RxnDictionary<T, V>` are a specialised use-case optimised for key/value stores
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
  * `NamedPipeBackingChannel`: uses the native operating systems IPC bus to send messages between processes
  * and more...
  
### 1.3.2. [Reactors](reactors.md)

* Event Managers and other [primitives](#event-driven-primitives) can be `chained` to reactors, communicating in an isolated way from other app components.
* These reactors couple components together to create resiliant, isolated, self-managed, micro-subsystems
* A scale out vector, as the `Reactor` grows, move it out of process without modifying a single line of code
* A reactors lifecycle is independant of your apps lifecycle, so they can be stopped or started without your app going down
* Mimics the supervisor pattern made popular by [Erlang](erlang.org)
* Automatically sets ups up metrics channel that provides deep insights into the event-flow of your system including the `back-pressure` of your reactions
  * Critical to understanding why your app is [event storming]()
  * Can be remotely viewed in real-time via the [AppStatus](#hosting) portal

### 1.3.3. Recording and playback

* Great tools to make your app more battle hardended
* Unlock advanced event sourcing features such as `undo`, `redo`
* Unit/Integration testing via tape playback
  * Automate integration test generation by recording user actions and storing in `ITapeRepository` for playback by the `UserAutomationPlayer`
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