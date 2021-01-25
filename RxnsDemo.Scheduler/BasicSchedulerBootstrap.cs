using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using Autofac;
using Rxns;
using Rxns.Health.AppStatus;
using Rxns.Hosting;
using Rxns.Interfaces;
using Rxns.Logging;
using Rxns.NewtonsoftJson;
using Rxns.Scheduling;
using Rxns.Windows;

namespace RxnsDemo.Scheduler
{
    public class BasicSchedulerBootstrap
    {
        static void Main(string[] args)
        {
            "Startup up basic scheduler integration example".LogDebug();
            RunBasicExample();

            //profit
            Console.ReadLine();

            RunAdvancedExample();
            "Starting advanced integration example".LogDebug();

            Console.ReadLine();
        }

        private static void RunBasicExample()
        {
            //basic integration
            var scheduler = new RxScheduler();
            var work = new SchedulableTaskGroup()
            {
                Name = "simple",
                IsEnabled = true,
                TimeSpanSchedule = TimeSpan.FromSeconds(10),
                Tasks = new ISchedulableTask[]
                {
                    new DyanmicSchedulableTask(state =>
                    {
                        "Task ran".LogDebug();

                        return state;
                    }),
                    
                }.ToList()
            };

            scheduler.ReportToDebug();
            scheduler.Start();
            scheduler.Schedule(work);
            
        }

        private static void RunAdvancedExample()
        {
            ///can conditionally execute tasks based on shared state that each task can reference for bindings or conditional execution
            var basicEtl = new SchedulableTaskGroup() //creates a logical grouping of tasks that combine to perform a domain need
            {
                Name = "BasicWorkflowExample",
                TimeSpanSchedule = TimeSpan.FromSeconds(10),
                IsEnabled = true,
                IsReporting = true, //enble appstatus integration
                Tasks = new ISchedulableTask[]
                {
                    new CmdTask(null)
                    {
                        Command = "cmd.exe /c echo {mapped} >> yeh.txt",
                        Conditions = new List<ExecutionCondition>() //can conditionally execute based on state/bindings and basic <>=!= logic operators
                    },
                    new FileCopyTask(null)
                    {
                        Files = new[] { "yeh.txt" }.ToList(),
                        Destination = "anotherDir"
                    },
                }.ToList()
            };

            RxnExtensions.DeserialiseImpl = (t, json) => JsonExtensions.FromJson(json, t);
            RxnExtensions.SerialiseImpl = (json) => JsonExtensions.ToJson(json);

            //tasks could be inline
            //.CreatesOncePerApp(_ => new InlineTasksProvider(basicEtl))
            //
            //but can also be read from an external file
            File.WriteAllText("tasks.json", new[] { basicEtl }.Serialise());

            new ContainerBuilder()
                .ToRxnsSupporting(cfg =>
                {
                    cfg
                        .Includes<SchedulingDependencyModule>() //add the scheduler
                        .CreatesOncePerRequestNamed<CmdTask, ISchedulableTask>() //add tasks
                        .CreatesOncePerRequestNamed<FileCopyTask, ISchedulableTask>()
                        .CreatesOncePerApp(cc => //magic to allow deserilise to resolve tasks from the app container
                        {
                            var tf = cc.Resolve<ITaskFactory>();
                            JsonExtensions.DefaultSettings.Converters.Add(new TaskCreationConverter(tf));
                            JsonExtensions.DefaultSettings.Converters.Add(new RxnsCreationConverter<SchedulableTaskGroup>(c => new SchedulableTaskGroup(cc.Resolve<IRxnManager<IRxn>>())));

                            return new RxScheduler();
                        })
                        .Includes<AppStatusClientModule>() //allows appstatus reporting of tasks/progress
                        .Includes<RxnsModule>() //sets up auto-reporting. Could omit and manually trigger heatbeats by calling .Ping() method directly
                        .CreatesOncePerApp<INSECURE_SERVICE_DEBUG_ONLY_MODE>();
                })
                .UseWindowsAddons()
                .Named(new AppVersionInfo("Micro scheduler", "1.0", true))
                .OnHost(new ConsoleHostedApp(), new RxnAppCfg())
                .Subscribe(instance =>
                {
                    instance.Resolver.Resolve<IRxnScheduler>().Start(); //startup the scheduler after any bootstrapping logic
                });
        }
    }
}
