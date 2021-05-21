using System;
using System.Diagnostics;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using Rxns.Health;
using Rxns.Logging;

namespace Rxns.Hosting
{
    /// <summary>
    /// A service that provides facilitates gathering operating system specific information on an 
    /// applications runtime environment, and manipulating this environment.
    /// </summary>
    public class MacOSSystemInformationService : ISystemResourceService
    {
        public IScheduler DefaultScheduler { get; set; }

        public IObservable<float> CpuUsage { get; }

        public IObservable<float> MemoryUsage { get; }

        public IObservable<AppResourceInfo> AppUsage { get; }


        private readonly IOperationSystemServices _systemServices;


        public MacOSSystemInformationService(IOperationSystemServices systemServices, IScheduler scheduler = null)
        {
            _systemServices = systemServices;
            DefaultScheduler = scheduler ?? System.Reactive.Concurrency.DefaultScheduler.Instance;

            //this can stay here because refcount doesnt activate the hot observable until
            //it is subscribed too. otherwise its not advised to have activation logic automatically
            //set here because of race-conditions when reporting - nothing will see the messages!


            AppUsage = GetUsage().Select(info =>
            {
                var p = Process.GetCurrentProcess();

                return new AppResourceInfo()
                {
                    Threads = p.Threads.Count,
                    Handles = p.HandleCount,


                    CpuUsage = info.CPU,
                    MemUsage = info.MEM
                };
            }).Publish().RefCount();

            CpuUsage = AppUsage.Select(a => a.CpuUsage);
            MemoryUsage = AppUsage.Select(a => a.MemUsage);
        }

        public class MacInfo
        {
            public float CPU { get; set; }
            public float MEM { get; set; }
        }

        public IObservable<MacInfo> GetUsage()
        {
            return Rxn.Create<MacInfo>(o =>
            {
                float cpu = 0;
                float mem = 0;
                var foundAllInfo = false;

                return Rxn.CreatePulse(TimeSpan.FromSeconds(1), () =>
                {
                    foundAllInfo = false;

                    Rxn.Create("top", "-l 1", i =>
                    {
                        if (foundAllInfo) return; //return as soon as we found the stuff we are after

                        if(i.StartsWith("CPU"))
                            cpu = ParseCpu(i);

                        if(i.StartsWith("Phys"))
                        { 
                            mem = ParseMem(i);

                            o.OnNext(new MacInfo()
                            {
                                CPU = cpu,
                                MEM = mem
                            });

                            foundAllInfo = true;
                        }

                    }, e => { e.LogDebug("TOP ERROR"); }).FinallyR(() => { }).Until(o.OnError);

                    return false;

                }).Until();
            });
        }

        public float ParseCpu(string topCpuInfo)
        {
            if (topCpuInfo.IsNullOrWhitespace()) return 0;

            var tokens = topCpuInfo.Split(new[] {  " " }, StringSplitOptions.RemoveEmptyEntries).Where(s => s.EndsWith("%")).Select(s => s.Substring(0, s.Length-1)).ToArray();
            var used = (tokens.FirstOrDefault().IsNullOrWhiteSpace("0").AsFloat() + tokens.Skip(1).FirstOrDefault().IsNullOrWhiteSpace("0").AsFloat());

            return used;
        }
          
        //var memInfo = "PhysMem: 5807M used (1458M wired), 10G unused.";
        public float ParseMem(string topMemInfo)
        {
            if (topMemInfo.IsNullOrWhitespace()) return 0;

            var tokens = topMemInfo.Split(new[] { " " }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Replace("M", "").Replace("G", "000")).ToArray();
            var used = tokens[1].IsNullOrWhiteSpace("0").AsFloat();
            var unused = tokens[5].IsNullOrWhiteSpace("0").AsFloat();

            return unused / (used + unused);
        }

        public void SetProcessPriority(int priority)
        {
            _systemServices.SetProcessPriority(priority);
        }

        public void SetProcessMaxCpuCoreUsage(double percentage)
        {
            var cores = _systemServices.GetSystemCores();
            var coresToUse = (percentage * cores);
            var maxCores = coresToUse < 1 ? 1 : GetBitMaskFor(coresToUse);

            _systemServices.SetProcessorAffinity((IntPtr)maxCores);
        }

        /// <summary>
        /// The ProcessorAffinity property is a bit mask variable. So, the values are:
        /// Value	Allowed processors
        /// 0 (0000)	Not allowed (that would mean use no processors)
        /// 1 (0001)	Use processor 1
        /// 2 (0010)	Use processor 2
        /// 3 (0011)	Use both processors 1 and 2
        /// 4 (0100)	Use processor 3
        /// 5 (0101)	Use both processors 1 and 3
        /// 6 (0110)	Use both processors 2 and 3
        /// 7 (0111)	Use processors 1,2 and 3
        /// 8 (1000)	Use processor 4
        /// </summary>
        /// <param name="coresToUse">The number of cores to use</param>
        /// <returns>The processor affinity bitmask. Note only 64bit machines can use more then 32cores</returns>
        private long GetBitMaskFor(double coresToUse)
        {
            var systemMax = Int64.MaxValue;

            if (coresToUse > 63)
                return systemMax;

            var maxCores = Math.Floor(coresToUse);

            for (int i = 63; i != maxCores; i--)
                systemMax = systemMax >> 1;

            return systemMax;
        }
    }
}
