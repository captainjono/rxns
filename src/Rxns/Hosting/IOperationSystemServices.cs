using System;
using System.Collections.Generic;
using System.Reactive.Concurrency;

namespace Rxns.Hosting
{
    public interface IOperationSystemServices
    {
        int GetSystemCores();
        void SetProcessPriority(int priority);
        void SetProcessorAffinity(IntPtr affinityMask);
        /// <summary>
        /// Returns the account for which a service is running under. Sometimes there are 
        /// situations where more then 1 service is found, and therefor more then 1 user is returned.
        /// </summary>
        /// <param name="name">The name of the service</param>
        /// <returns>The logon as service credentials</returns>
        IEnumerable<string> GetServiceUsers(string name);

        /// <summary>
        /// Executes a shell command against the operating systems shell feature
        /// </summary>
        /// <param name="shellCommand">The command to execute</param>
        /// <param name="parameters">The commannd parameters, if any</param>
        /// <param name="scheduler">The scheduler used to run the command, if any. The default is the Scheduler.CurrentThread</param>
        /// <returns></returns>
        IObservable<string> Execute(string shellCommand, IScheduler scheduler = null);

        void AllowToBeExecuted(string appName);
    }
}
