<!-- TOC -->

1. [Rxn Scheduler](#rxn-scheduler)
   1. [Basic example](#basic-example)
   2. [Rxns Integration](#rxns-integration)

<!-- /TOC -->

# Rxn Scheduler

If a fully features scheduler that provides:
*  Basic workflow capabilities
*  Organises tasks into groups
*  Schedules on timespans
*  Conditionally execute of tasks based on the outcomes of other tasks or execution state
*  *  Configure tasks to fail on error or not
   *  Even once running, can wait for *other* groups to finish before triggering *any subtask*
* Basic binding support
  * Each groups share state which can be `{bound}` to any property of a task
  * Can update bindings via `Input/OutputParmeters` *(task outcomes)* at runtime

## Basic example

```cs
  var scheduler = new RxScheduler();
  var work = new SchedulableTaskGroup()
  {
      Name = "Simple",
      IsEnabled = true,
      TimeSpanSchedule = TimeSpan.FromSeconds(10),
      Tasks = new ISchedulableTask[]
      {
          new DyanmicSchedulableTask(state =>
          {
              "Task Executed!".LogDebug();

              return state;
          })                    
      }.ToList()
  };

  scheduler.ReportToDebug(); //logging
  scheduler.Start();
  
  //can schedule before or after start
  scheduler.Schedule(work);

  //anytime later, waits till completion before stopping
  scheduler.Unschedule(work);
  scheduler.Stop();
```

## Rxns Integration

* Can be used with the `AdaptiveScheduler` to provide support for hot-swapping tasks at runtime with various `ITaskProviders`
* Tasks/groups support DI, so they can be configured via a `RxnContainer` by convention
* Publishes task progress to the [AppStatus]() portal for remote monitoring
  * Including
    * Duration
    * Current step
    * Errors
  * Remotely `start/stop` schedules with the `CommandService`
