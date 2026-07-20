using System;
using Rxns.Interfaces;

namespace Rxns.Health
{
    public class AppResourceInfo : HealthEvent
    {
        public float MemUsage { get; set; }
        public long Handles { get; set; }
        public long Threads { get; set; }
        public float CpuUsage { get; set; }
        public string Host { get; set; }
        public string Name { get; set; }

        public AppResourceInfo ForHost(string host, string name)
        {
            Host = host;
            Name = name;

            return this;
        }
    }

    /// <summary>
    /// A service that provides access to operating system specific resources
    /// </summary>
    public interface ISystemResourceService
    {
        /// <summary>
        /// Returns a hot observable sequence of the overall CPU time. Updated every second.
        /// </summary>
        IObservable<float>  CpuUsage { get; }
        /// <summary>
        /// Returns a hot observable sequence of the overall physical Memory usage. Updated every second.
        /// </summary>
        IObservable<float> MemoryUsage { get; }
        /// <summary>
        /// Returns a hot observable sequence of the overall app usage. Updated every second.
        /// </summary>
        IObservable<AppResourceInfo> AppUsage { get; }

        /// <summary>
        /// Set the scheduling priority of the currently executing process.
        /// By default, all processes run at normal priority
        /// Higher priorities get executed berfore lower by the operating system 
        /// </summary>
        /// <param name="priority">The priority to set for the current process</param>
        void SetProcessPriority(int priority);

        /// <summary>
        /// Set the scheduling priority of the currently executing process.
        /// By default, all processes run at normal priority
        /// Higher priorities get executed berfore lower by the operating system 
        /// </summary>
        /// <param name="priority">The priority to set for the current process</param>
        void SetProcessMaxCpuCoreUsage(double percentage);
    }
}
