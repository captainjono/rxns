# Rxn defintiions

All RxnApps start with a definition. These are the services that the App composes together to create its domainAPIs and supporting services. 

```c#
public interface IRxnDef
{
    IAppContainer Container { get; }
    IRxnDef UpdateWith(Action<IRxnLifecycle> lifecycle);
    void Build(IAppContainer updateExisting = null);
}
```

They are composed with the use of an AppContainer. Containers are used to control your Apps resources. They implemnet the IoC/DI pattern to create a Apps that can be quickly recomposed to suite a variety situations and platforms *with only* basic configuration changes. 

```c#
  public static class OutOfProcessDemo
    {
        public static Func<Action<IRxnLifecycle>, Action<IRxnLifecycle>> DemoApp = d =>
        {
            return dd =>
            {
                d(dd);    
                dd.CreatesOncePerApp<OutOfProcessCache>();
                dd.RespondsToSvcCmds<LookupReactorCount>();
                dd.RespondsToSvcCmds<LookupReactorCountQry>();
                dd.RespondsToSvcCmds<IncrementReactorCount>();
                dd.RespondsToCmd<IncrementReactorCount>();
                dd.RespondsToQry<LookupReactorCountQry>();
                
            };
        };
    }
```

Rxns comes packed with `AppModules` which are collection of IRxnLifecycle registerations that can be compose together as building blocks to create hyper specialised micro Apps that can be goegraphically distrbuted, cross platform, or hyper-scaled cross multi-processes by simply injecting different combinations of components.

> See: [RxnModules](buildingblocks.md)

```c#

```


Traints & Patterns
* Enforces SoC |  By not allowing the concrete implementation of classes to muddy the domainAPI. 
* Clearly define the domainAPI | by enforcing access to it via interfaces that are registered with it via the RxnApp.Definition
* COntrol the lifecycle of components and track and control the dependencies that your domainAPIs have
* 

