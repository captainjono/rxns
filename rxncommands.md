# Commanding *Reactions*

Ubitqitious today is the requirement for being able to not only build decoupled and goegraphically distrbuted systems, but also to command them in finely grained ways in a varity of situations. 

Reasons to command:
- Accepting user input such as traditional CRUD 
- [Scheduling](scheduler.md) or running background tasks that monitor queues, sync databases etc
- 

Often you will end up unwittingly creating bespoke commanding systems throughout your App, often convertly, as it grows. Reactions goes all in on events, and as such, commanding via events is the first class pattern the system supports. These endpoints are then exposed over a varieity of different transport mechanisms such as

> Read more [about hosts here
host | support
-|-
WebAPIHost | RESTful
ConsoleHost | Command line while app is running
SlothBot | 



In reactions, commands have been reasoned about from a variety of use-cases and subsystems have been put in place such that if you follow the general [patterns & practices](patterns.md) encourged in the building blocks, your App will be fully capable of handling the following supported commanding styles:

## Command Types
Command | Execution Vector | Usage
-|-|-
Domain Command | Async Commands and Querys for your [ddd aggregates](dddaggs.md)
Service Command | Command your Apps micro-components, [reactors](reactors.md), to start and stop
AppCommand | App console, [AppStatus](appstatus.md) | Parse and run commands from any string **NOTE: TRUSTED INPUT ONLY**

### Service Commands

These are light weight commands that are used to control the different internal services of your app.
* They must support parsing from strings
  * They must have constructors which accept all string params to init over the wire

## Command Execution

> CAUTION: 
> Always SECURE YOUR COMMANDS. 
> 
> *Dont expose debugging channels to the open internet*
> 
> **Use** [certificates or keys](securingRxns.md) to authenticate before allowing remote command execution.


Just as supporting commanding is important to your Apps maintaince burden over time, so is the way in which commands can be run. In cloud or cross platform environments, often you will be developing remotely to where you code is running so being able to quickly and easily develop and test and monitor commands in your system over time is as important as the APIs which faciliate the.


Execution Stratergy | Supports | Reason | Usage
-|-|-|-
`ICommandService` | All commands | Execute commands via code targetting [InProcess or WebApiHosted](rxnhosts.md) | App pipelines, Micro-services, JS/TS or other lanugage based other Micro-frontends
`IRxnManager` | All commands | Allows execution of commands received over any [BackingChannel](reactors.md) | ChatOps, Remote commanding, Debugging
[AppStatus](appstatus.md) | All commands | Execute commands via any browser | Debugging, Ops, Developing
`SlothBot` | All Commands | Execute commands via [Slack](www.slack.com) or [MS Teams](www.msteams.com) | ChatOps, Scrum, Debugging, Developing