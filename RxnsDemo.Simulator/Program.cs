using System;
using System.Reactive.Linq;
using Rxns;
using Rxns.Logging;
using RxnsDemo.Micro.Tests;

namespace RxnsDemo.Simulator
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            var usingTaskPool = RxnSchedulers.TaskPool;

            var doExam = new StudentsUsingTheTestSessionSimulator(TimeSpan.FromMinutes(10), 100, 23, TimeSpan.FromSeconds(5), usingTaskPool);
            doExam.ReportToDebug();
               
            doExam.Start()
                .Do(cmd =>
                {
                    //stream commands to console to see whats going on
                    //would need to hookup to a AppCommandServiceClient to 
                    //run the commands against an actual instance of the API
                    cmd.Serialise().LogDebug("[Sim]");
                })
                .Subscribe(_ => { });

            Console.Read();
        }
    }

}
