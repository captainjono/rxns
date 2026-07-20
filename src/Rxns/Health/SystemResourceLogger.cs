using System;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns.Health
{
    /// <summary>
    /// Reports on the CPU usage and memory utilisation of the current machine
    /// to the information channel
    /// </summary>
    public class SystemResourceLogger : ReportsStatus 
    {
        private readonly ISystemResourceService _systemResources;

        public SystemResourceLogger(ISystemResourceService systemResources)
        {
            _systemResources = systemResources;

            _systemResources.CpuUsage.Subscribe(usage =>
            {
                OnInformation("CPU: "+ usage);
            }).DisposedBy(this);

            _systemResources.MemoryUsage.Subscribe(usage =>
            {
                OnInformation("Memory: " + usage);
            }).DisposedBy(this);
        }
    }
}
