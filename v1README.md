#  Introducing .... *Rxns* 
![Rxns logo](https://github.com/captainjono/rxns/blob/master/logo.png "Reactions Logo")
(pronounced: **Reactions**)

A C# framework for building highly specialised, testable, event driven *MicroApps* across the stack with [Reactive Extensions *(Rx)*](http://reactivex.io/) 

[![NuGet](https://img.shields.io/nuget/v/Rxns.svg)](https://nuget.org/packages/Rxns)

## In a nutshell ...

1. *Fluent first*: Because maintaining documentation is hard and Rx has a domain learning curve
```csharp
//On Reaction to A Todo Item being Created, show a busy signal to the user until its saved
this.OnReactionTo<ATodoItemCreated>()
        .UpdateUIWith(ShowBusySignal)
        .SelectMany(SaveTodo)
        .UpdateUIWith(HideBusySignal)
        .Until(ohHide);

this.OnReactionTo<CpuOverThreshHold>().Do(SendAlertToSam).Until(FatalError);
```

1. *Composable Actors*: Inspired by [Erlang](https://www.erlang.org/), reactions are chain'ed to Reactors which supervise them
```csharp

public class TodoOrchestrator : IRxnProcessor<ATodoItemCreated> { ... }

//note: u never have to write this code if you use an IoC container like Autofac, reactions 
//are hooked up auto-magically
var todoReactor = new Reactor("TodoCriticalServices");
var orchConnection = todoReactor.Connect(todoOrchestrator, todoReactor);
var pushNotiConnection = todoReactor.Connect(todoPushNotificationService, todoReactor);
var fireDoctor = todoReactor.Monitor(autoRestartAfter10ErrorsDoctor);

//...later an event storm occours...
orchConnection.Dispose(); //take the orchestrator offline, but leaves push notifications up 
```

1. *Testable*: We beleive your event streams should form the basis of regression testing
```csharp
public interface ITapeRecorder
{
    PlaybackStream Play(ITapeStuff tape, PlaybackSettings settings = null);
    IDisposable Record(ITapeStuff tape, IObservable<IRxn> stream);
}

public interface IAutomateUserActions
{
    IObservable<bool> AutomateUserActions(Page page, RxnPageModel model, IObservable<IRxn> actions, Action<IRxn> publish);

    ITapePlaybackFilter[] Filters { get; }
}
```

1. *Real-time*: Understanding what your reactions are doing in an event storm should not be hard
```csharp
// this chart is automatically compiled for u, and uses colours and sizes to display 
// throughput / backpressure / status of ur reactions, others stream your IReportsStaus log
// and in the future, remote control, scaling and commanding 
```
![Metrics Graph](https://github.com/captainjono/rxns/blob/master/examples/metrics.png "Metrics Console")

## Dive a little deeper

### Rxns 
##### *On the UI* with the MV* pattern

**ViewModel**
```csharp
this.OnReactionTo<AnEvent>.Select(SomethingRemotely).UpdateUIWith(TheResult).Until(FormIsHidden);
...
public string Name { get; set;} //impelements IPropertyChanged
this.OnReaction(this, p => p.Name).Do(CallAnApi).Until(ohHide)
...
public LoginCmd {get; set;}
LoginCmd = this.OnExecute(loginDetails => _publish(new UserAttemptingLoginEvent>(loginDetails));
```

**View**
```Xaml
<Label Value={Binding Name}/>
<Button Command="{Binding LoginCmd}" 
	CommandParameter={Binding LoginDetails}
	/>
```

#### On the Server

```csharp
public class ServerEventProcessor : IRxnProcessor<UserAttemptingLogin>
{
    //pure functions are encouraged
    IObservable<IRxn> Process(UserAttemptingLogin loginDetails) 
    {
        //Func<bool>, IObservable<bool>, with error handling covered
        return Rxn.Create(() => _loginService.Verify(loginDetails)) 
                            .Select(success => success 
                                ? new LoginSuccessfull(loginDetails.Username)
                                : new LoginFailure(loginDetails.Username));
    }
}
```

### Rxns cluster in Reactor's
Keep critical services apart, or index by a microApps feature *optionally* by implementing IRxnCfg
```csharp
public SomeClass : WhateverIWant, (... IRxnCfg)
{
	/// <summary>
        /// The name of the reactor that this reaction will be hooked up to. 
        /// Null specifies the system will use the default reactor
        /// </summary>
        string Reactor { get; }
        /// <summary>
        /// Configures the input pipeline that is feeds any reations implemented by this
        /// class/interface. returning the pipeline fed into this method is the equivilant
        /// of not doing anything to it.
        /// </summary>
        /// <param name="pipeline"></param>
        /// <returns></returns>
        IObservable<IRxn> ConfigureInput(IObservable<IRxn> pipeline);
        /// <summary>
        /// The delivery scheme used to control the events that are observed on the input pipeline. 
        /// Null disables using any delivery scheme which.
        /// </summary>
        IDeliveryScheme<IRxn> InputDeliveryScheme { get; }
        /// <summary>
	/// Gets your status supervised
        /// </summary>
	bool MonitorHealth { get; }
}

```

### Reactors communicate with RxnManagers
which are pub/sub based
```csharp										
var unsubscribe = rxnManager.CreateSubsciption<ToAnEvent>().Do(theEvent => { ... }).Until(); 
rxnManager.Publish(new AnythingThatImplementsIRxn());
```
that use an abstraction for the transport medium so you can scale you apps

```csharp
public class LocalBackingChannel : IBackingChannel { ... } //batteries-included, pibbybacks off Rx.Subject<>
```

```csharp
public class AzureBackingChannal : IBackingChannel { ... } //...or AWS, Akka, Orealens - add them at your will
```

```csharp
public class MessagingCenterBackingChannel : LocalBackingChannel { ... } // Legacy Xamarin code
```

## We use interfaces to magically wire everything up 
to reduce coupling and also encourage the use of patterns encouraged by the masters
1. https://martinfowler.com/eaaDev/EventSourcing.html
1. http://udidahan.com/2009/06/14/domain-events-salvation/

```csharp
IRxnPulser<SomeEvent> //a reaction which produces values at set intervals
```
ie.
```csharp
public class ET : IRxnPulsar<IPhoneHomeEventsNearEndOfMovie> 
{
	public Interval { get { return TimeSpan.FromSecond(10); } }
}
```

or
```csharp
IRxnPublisher //a reaction that can publish at will with _publish(new SomethingHappened());
```
.ie
```csharp
public class Scoreboard : IRxnPublisher<ScoreUpdate>
{ 
	...
	public override void ConfigurePublishFunc(Action<ScoreUpdate> publish) 
	{
		_publish = publish;
	}
}
```

or 
```csharp 
IRxnProcessor //as described in the inital example, for Rxns which produce 0 or more Events as a result
```

## We provide various helpers 
that make event sourced apps easier to reason about 

### With a functional programming style
```csharp
//update a cache directly from your event stream
var modelCache = new RxnDictionary<TEvent, TKey, TValue>(new YourDictionaryImplementation(), 
								this.OnReactionTo<TEvent>(),
								(event, dict) => { dict.AddOrUpdate(event.Key, event.Value) }); 
...
//event source anything with a method
var nowEventSourced = RxnDecorator<MouseMoved, LegacyUIPainter>(this.OnReactionTo<MouseMoved>(),
								new uiPainter(),
								(mm, uiPainter) => uiPainter.paintScreen(mm.X, mm.Y))

//traditional queues for multi-tenant envIRxnonments
public class AnEventSourcedQueue : ShardingQueueProcessingService<AnImportantTask>
{
    public override IObservable<CommandResult> Start(string @from = null, string options = null)
    {
        return Run(() =>
        {
            var allTenants = _tenants.GetAll()
					.Select(tenant =>
					{
						return new Func<AnImportantTask, bool>(i =>
						{
							return i.Tenant == tenant;
						});
					})
					.ToArray();

            _queueWorkerScheduler = TaskPoolSchedulerWithLimiter.ToScheduler(allTenants.Length > 0 ? allTenants.Length : 8);
            StartQueue(allTenants);
        })
    
    }
}

```

### And remember you get metrics for free
Whenever u specificy MonitorHealth = true in IRxnCfg
![Events per second](https://github.com/captainjono/rxns/blob/master/examples/eventsPerSecond.png "Reaction throughput Per Second")

### We consider logging a first order concept ...
*IReportStatus*, *ReportStatus*, *ReportStatusService* all provide Information & Error channels as well as IReportHealth that links up to metrics 

```csharp
public MyClass : ReportsStatus { }

var myClass = new MyClass();

//can use any of the below
myClass.ReportsToConsole();
myClass.ReportsToDebug();
myClass.Errors.Do(SendAlert).Until(AppTerminated);
myClass.Information.Do(LogToWeb).Subscribe() // if u prefer Rx syntax;

//or log for someone else
var supervisor = new Supervisor();
myClass.ReportsWith(supervisor)
superVisor.ReportToConsole()

//can also use OnVerbose, OnWarning, OnErrors - api designed for .net4
myclass.OnInformation("A general {0}", "message");
//On console -> [ThreadId] [10:55:12.12] [Information] [MyClass] A general message
...

var rxnDictionary = new RxnDictionary<..>();
rxnDictionary.SubscribeAll(info => { //info }, error => { SmsMikeAt1AM(error)})


//we make Rx safer for, if the subscription function throws an exception, its reported on the OnError 
//channel of *this Reporter* instead of crashing ur app
rxnDictionary.OnUpdate()
			.Buffer(10)
			.Subscribe(this /*IReportStatus*/, @events => { CaptureEventsForLater(events); })

```

### ... just as we do testing 
Record your event streams in different scenarios and assert on them over and over again
```csharp
var tape = RxnTape.FromSource("LoginTest", source);
var recording = tapeRecorder.Record(tape, this.OnReactionTo<UserAction>();

//make system do stuff, then later

recording.Dispose(); 

//and now in your test suite

var player = new UserAutomationPlayer();
var output = player.Play(tape, new PlaybackSettings { TickSpeed = 10 } //fast forward

//play back all to the RxnManager as they were recorded
output.Stream.Do(_publish).Subscribe()
```

**Built in UI testing for Xamarin / any other .NET app**
```
// RxnUI Pattern (aka. Event Sourcing)
// 1. Every action must start with an event
// 1. Events triggered by Events should be marked as IReactiveEvent
```
If your follow the RxnUI pattern correctly, u can build UI tests on an actual device by using the app u are recording. Uneducated UI testers can take snapShots on demand which assert when you play them back. 

**Bonus** 
Use can use the same automation technique to 
- explain features to users.
- mostly eliminate no-repo bugs
- create a bot army that tests the scaling ability of your apps(!!)
***coming soon**

### We dont just log **backpressure**, we mitigate it
Rxns that are getting backed up can DoS your users (input is quicker then the Process/Rxn chain method can process it)

```csharp
rxn.BufferFirstLast/Distinct(); //drop elements from a sequence if they arrive to quickly, 
				//are repeating, or you only care about the inital or last value 

//soon for lossless event streams
rxn.OverflowTo(AzureTable).When(backpressure => backpressure > 1000 /*events*/).RequeueWhen(backpressure < 100);
```

### Thats enough to get you started
Now go have a play and look at the API... detailed doco comming soon

## Features in the pipeline ...

1. *CmdService*: to build real-time microservices without the boiler-plate
1. *Event Sync*: To support occasionally connected MicroApps
1. *JS/Typescript bridge*: A pattern to build ES Domain Aggregates that are universal (share with [React](https://reactjs.org/), [Angular 2](https://angular.io/) etc.)
1. IPhoneHome just like E.T with *AppStatus* always watching you app for anomolies
1. *Serious*: If you outgrow the Queues or Buses of cloud providers, swap to [Akka](https://getakka.net/) or [Orleans](https://dotnet.github.io/orleans/) on a per Reactor basis.
