using System;
using System.Diagnostics;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using Rxns.Hosting;

namespace Rxns.Health
{
    /// <summary>
    /// A service that provides facilitates gathering operating system specific information on an 
    /// applications runtime environment, and manipulating this environment.
    /// </summary>
    public class WindowsSystemInformationService : ISystemResourceService
    {
        public IScheduler DefaultScheduler { get; set; }

        public IObservable<float> CpuUsage { get; }

        public IObservable<float> MemoryUsage { get; }

        public IObservable<AppResourceInfo> AppUsage { get; }

        private readonly PerformanceCounter _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
        private readonly decimal _systemTotalMemory = PerformanceInfo.GetTotalMemoryInMiB();
        private readonly IOperationSystemServices _systemServices;
        

        public WindowsSystemInformationService(IOperationSystemServices systemServices, IScheduler scheduler = null)
        {
            _systemServices = systemServices;
            DefaultScheduler = scheduler ?? System.Reactive.Concurrency.DefaultScheduler.Instance;

            //this can stay here because refcount doesnt activate the hot observable until
            //it is subscribed too. otherwise its not advised to have activation logic automatically
            //set here because of race-conditions when reporting - nothing will see the messages!
            CpuUsage = Rxn.CreatePulse(TimeSpan.FromSeconds(1), () => _cpuCounter.NextValue(), DefaultScheduler).Publish().RefCount();
            MemoryUsage = Rxn.CreatePulse(TimeSpan.FromSeconds(1), () => GetMemoryUtilisation()).Publish().RefCount();
            AppUsage = Rxn.CreatePulse(TimeSpan.FromSeconds(1), () =>
            {
                var p = Process.GetCurrentProcess();

                return new AppResourceInfo()
                {
                    Threads = p.Threads.Count,
                    Handles = p.HandleCount,
                    MemUsage = (float)Process.GetCurrentProcess().VirtualMemorySize64 / (int)Math.Pow(1024, 3)
                };
            }).Publish().RefCount();
        }

        public float GetMemoryUtilisation()
        {
            var ratio = (PerformanceInfo.GetPhysicalAvailableMemoryInMiB() / _systemTotalMemory) * 100;

            return (float)(100 - ratio);
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

    public static class PerformanceInfo
    {
        [DllImport("psapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetPerformanceInfo([Out] out PerformanceInformation PerformanceInformation, [In] int Size);

        [StructLayout(LayoutKind.Sequential)]
        public struct PerformanceInformation
        {
            public int Size;
            public IntPtr CommitTotal;
            public IntPtr CommitLimit;
            public IntPtr CommitPeak;
            public IntPtr PhysicalTotal;
            public IntPtr PhysicalAvailable;
            public IntPtr SystemCache;
            public IntPtr KernelTotal;
            public IntPtr KernelPaged;
            public IntPtr KernelNonPaged;
            public IntPtr PageSize;
            public int HandlesCount;
            public int ProcessCount;
            public int ThreadCount;
        }

        public static Int64 GetPhysicalAvailableMemoryInMiB()
        {
            var pi = new PerformanceInformation();
            if (GetPerformanceInfo(out pi, Marshal.SizeOf(pi)))
            {
                return Convert.ToInt64((pi.PhysicalAvailable.ToInt64() * pi.PageSize.ToInt64() / 1048576));
            }

            return -1;
        }

        public static Int64 GetTotalMemoryInMiB()
        {
            var pi = new PerformanceInformation();
            if (GetPerformanceInfo(out pi, Marshal.SizeOf(pi)))
            {
                return Convert.ToInt64((pi.PhysicalTotal.ToInt64() * pi.PageSize.ToInt64() / 1048576));
            }

            return -1;
        }
    }
}
