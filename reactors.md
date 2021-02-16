# Reactors
A
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