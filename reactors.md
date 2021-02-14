# Reactors

<!-- TOC -->

- [1. Reactors](#1-reactors)
- [2. Orchestration Services](#2-orchestration-services)
- [3. ReliabilityManager](#3-reliabilitymanager)
- [4. Why?](#4-why)
    - [4.1. reACTORS](#41-reactors)
- [5. Specifics](#5-specifics)
- [6. How](#6-how)
- [7. Reactors in a RxnsApp](#7-reactors-in-a-rxnsapp)
- [8. Types of reactors?](#8-types-of-reactors)
    - [8.1. Examples of typical reactor achitecture](#81-examples-of-typical-reactor-achitecture)

<!-- /TOC -->


RxnsApp 

- Domain API 
- Patterns and practices

# Orchestration Services
*Usage*
- For the coordination *or* 'glueing' or two or more `Domain APIs` together. Espically useful when these API span processes / machine boundaries,This layer is purely concerned with the mechanics of making *this* happen and can be reasoned about as specialised type of an `anti-corruption layer`.
  - *Complimentary patterns*
  + [ReliablebilityManger](#reliabilitymanager) | delegate the actual execution of the `DomainAPI` calls to this service too bring a consistant UX to your App in a controlled and thoughout manner.
  
*Charactoristics displayed*
- Can be reasoned about as a Adapter which has buisness logic only to deal with making the glue between the apis more reliable
- Examples
  - `ViewProcessor` | a general type of 
  *- Specialised*
    - `DatabaseViewProcessor`
      - Uses events to offload work from a database centric app pipeline that 
        1. displaying poor inconsistant performance sporadically while under load
        2. is expensive to run on hardware that seems to be underutilised most of the time
      
      - *How?* The service acts as a middle man, writes to fast durable medium, like an event stream and returns the result to the user immediately. This streams is consumed by the processor and projected into a database. This projection is buffered / throttled to desired `cost/speed` ratio of the concerned service.
      > #StrangleTheMonolith | Allows the database to be spec'd lower then the maxium throughput of the system, but not be overwelhmed by peak loads. `Event streams` / `storage queues` are *orders of magnatude cheaper* to run the `cloud databases`
  - `UIOrchestration service`: This type of orchestrator is concerned primarily with translating UserEventStreams into UI actions in cases where event sourcing / one-way data flow driven `RxnCommands` or the `RxnManager` with the `IReactTo<IRxn>` interface are used to coordate an event-flavoured MVVM/MVC pattern.

# ReliabilityManager
  All RxnApps operations should be funneled through the reliability manager. This does mean it becomes a single point of failure, rather, it acts as quality gateway to make your apps operations consistant and durable.  
  - This pattern allows you to seperate the transport mechanism from the actual data you wish to transfer
  - These patterns allow you to implement consistant relability schemantics without the users of the API every reall knowing. This is an advantage in large teams or scenarios when the codebase is worked on by many people of differing Domain expertise. 
-Coordinates the always on nature of a RxnsApp, and makes it resilliant to cloud native conditions such as transiant failures in connection durability.
-This feature is implemented using cut down version of `Polly`


types of reliabiliy services
Each reliability operation can be configured with its own RetryPolicy that cator for occasionally cloud environments
  - Retry policies:
    - Exponential backoff
    - Linaear backoff
    - Circut breaker
    - Fallback operations
  - Types of standard RxnApp services
      -  `CallOverHttpForever(httpClient => {})` : provides a reliable http connection to the caller so they only need to care about what they are sending, not how. 
      -  `CallDatabase(sqlClient => {})`: provides a reliable database connection to the consumer which cators for standard error / retry scenarios.
    - 
  - CallDatabase() 
  - THis interface abstracts away the authoriations, location and 


# Why?

Every reactive application can be thought of as a *set of reactions*. When something happens, something is told todo something and so on to create the domain logic of your app. As apps grow, these boundaries can become blurred if care is not taken, causing issues with maintainence costs, scalability, event storms, a other... chain reactions. Once you build your reactive app, taming it becomes the next challenge. Reactors are a pattern to bring order to your app.

Reactors benifits include: 
- Isolatation boundaries in your Reactive application that allow Domain specific services start, stop and independently of eachother at any point in time, without a user ever knowing.
- Horitizontal scaling point for your app, allowing it to adapt to whatever core count or user load your app generates.
- Faciliate decoupling of your app
- Stop and start independtly of eachother, including at any point during runtime
- Can scale independently of eachother, including being sent [Out Of Process] or [Into the Cloud]   

## reACTORS

Reactors are a modern take on other actor frameworks like `Erlang`, `Lisp`, `Akka` and `Orleans` that was designed to utilise Cloud Native technologies instead of aimed at on-prem or other self-managed infrastructure. The aim of reactors ecapsulate your App's buisness domains from being polluted by the semantics of the technological domain where they put the bytes to the streams and actually run. This adapting of domains acts as a natural barrier that without intrusion limits the use-case to the point where seemlessly move from host to host later on with fluid motion. Your app can go from single threaded to multi by to simpling running it on a `ClusteringAppHost`. Hosts can be composed together to create multi-threaded-cloud-functions. Learn more about [AppHosts  here](#AppHosts)

# Specifics

Each reactor has:
- RxnManager: An event-driven backbone that supports pub/sub
  - You can adapt reactors to different platforms / scaling scanarios via its BackingChannel
- Molecules: These are the services which are chained to the reactor

# How

You can create Reactors in two ways, either via code, or dynamically from the terminal.

* via code: `IRxnPublisher`, `IRxnProcessor`, `IRxnPulsar`, `IReactTo`, using the `IRxnManager`, or using the `ICreateReactor`/`IManagerReactor` Api directly [examples]*
  * At startup, reactors are built automatically for you if you by the `ICreateReactors` service
* via terminal: `Rxn.create ReactorFrom <pathToExistingApp>`

# Reactors in a RxnsApp

All RxnApps have a reactor at there core (*pun intended*). This is called the main reactor and you can think of it like the backbone of your app. Only keep essential services chained here as this is the the heart of your app and usually it should usually onlu  ochestrate other actors. 

```
```

You then peel off layers of your achicture, and group the serivces that depend on eachother or are critical to the nature of a specific task inside other reactors like "Cache" "DataBase" "Orders" or "UserManagement".

```
```

# Types of reactors?

Once you start down that rabbit hole, its hard to come back out. Here is some inspiration

## Examples of typical reactor achitecture

Scaling out?

From an existing app

```
Rxn.create ReactorFrom "pathToApp"
```

From an app update
```
Rxn.create FromAppUpdate "pathToApp"
```
Reactors work best with Rxn:
```

```
IRxnCfg
```





```

```