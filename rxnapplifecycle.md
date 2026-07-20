# App Definitions

Central to the idea of a MicroApp is conciese set of clearly defined components which compose its buisness logic and other functionality. In reactions, this *chasis* is engineerd with a series of *IAppLifecycles* which compose together to form the final definition of your *reaction App*. 

```c#
    public interface IRxnDef
    {
        IAppContainer Container { get; }
        IRxnDef UpdateWith(Action<IRxnLifecycle> lifecycle);
        void Build(IAppContainer updateExisting = null);
    }
```

The `App Container` is the abstraction mechanism that supports loosely coupling your buisness logic layer to your technical logic layer, allowing them to morph independantly to best suite each seperate concerns needs as your *Apps* scaling demands are pushed.

Functional App Definitions / Pipelines are simple to create as all your need is a `Acton<IRxnLifeCycle>` to start your Micro App journey:

```c#
        // Here is an App definition factory that allows you compose base layers
        // together with specific implementations
        public static Func<StartUnitTest, Action<IRxnLifecycle>, Action<IRxnLifecycle>> TestAgent = 
        (cfg, d) =>
        {
            theBfg.testcfg = cfg;
            return dd =>
            {
                d(dd); //allows 
                dd.CreatesOncePerApp<theBfg>();
            };
        };
```
*An example definition for a TestAgent that is configured with a functional definition *

This technique is a powerful way to compose decrete units of building blockers togther with a simple boostrapper that allows you to layer functionality so you can create sets of MicroApps using the same shared librarys and components over and over again

Here is a boostrapper which takes advantage of this factory:

```c#
// compose our definition together with that of 
// classic "SpareReactor"
// see: RxnApp.cs for more prebuilt Apps that you can base
// mix-in powerful functionlaity into any App

 return TestAgent(new StartUnitTest() { /* runtime cfg */ }, RxnApp.SpareReator(url))
```

and here is a complete App definition:

```c#
    // this is a chassis for an Event Sourced App
    public static Func<string, Action<IRxnLifecycle>> SpareReator = appStatusUrl => spareReactor =>
    {
        appStatusUrl ??= "http://localhost:888";

        spareReactor
            .Includes<AppStatusClientModule>() //adds in ability to heartbeat to AppStatus
            .Includes<RxnsModule>() //adds in Rxns sauce to unleash Reactor magic into your App
            .CreatesOncePerApp<NoOpSystemResourceService>() //turns OFF CPU & mem reporting to AppStatus
            .CreatesOncePerApp(_ => new ReliableAppThatHeartbeatsEvery(TimeSpan.FromSeconds(10))) //interval to pushing metrics to AppStatus
            .CreatesOncePerApp<INSECURE_SERVICE_DEBUG_ONLY_MODE>() //allows you to use http://
            .CreatesOncePerApp(() => new AggViewCfg()
            { 
                ReportDir = "reports" //the directory where metrics data is persisted
            })
            .CreatesOncePerApp(() => new AppServiceRegistry()
            {
                AppStatusUrl = appStatusUrl //the url of AppStatus
            })
            .CreatesOncePerApp<UseDeserialiseCodec>(); //uses the .Deserialise() & Serilise() methods when perisisting metrics to disk
    };
```

