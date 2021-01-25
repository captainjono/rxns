using System;
using System.Reactive;
using Rxns.Health;
using Rxns.Logging;

namespace Rxns.Hosting
{
    public class NoOpSystemResourceService : ReportsStatus, ISystemResourceService
    {
        public IObservable<float> CpuUsage { get; private set; }
        public IObservable<float> MemoryUsage { get; private set; }
        public IObservable<AppResourceInfo> AppUsage { get; }
        public IObservable<int> ThreadCount { get; private set; }
        public IObservable<float> OurMemUsage { get; }

        public NoOpSystemResourceService()
        {
            CpuUsage = ((float)0).ToObservable();
            MemoryUsage = ((float)0).ToObservable();
            OurMemUsage = ((float)0).ToObservable();
            ThreadCount = 0.ToObservable();
            AppUsage = new AppResourceInfo().ToObservable();
        }

        public void SetProcessPriority(int priority)
        {
            OnVerbose("No op implementation");
        }

        public void SetProcessMaxCpuCoreUsage(double percentage)
        {
            OnVerbose("No op implementation");
        }
    }
}
