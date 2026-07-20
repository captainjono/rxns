using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Runtime.InteropServices;
using Rxns.Logging;

namespace Rxns.Hosting
{
    public class CrossPlatformOperatingSystemServices : IOperationSystemServices
    {
        public int GetSystemCores()
        {
            return Environment.ProcessorCount;
        }

        public void SetProcessPriority(int priority)
        {
            "SetProcessPriority Not implemented".LogDebug(this);
        }

        public void SetProcessorAffinity(IntPtr affinityMask)
        {
            "SetProcessorAffinity Not implemented".LogDebug(this);
        }

        public IEnumerable<string> GetServiceUsers(string name)
        {
            "GetServiceUsers Not implemented".LogDebug(this);

            return new string[0];
        }

        public IObservable<string> Execute(string shellCommand, IScheduler scheduler = null)
        {
            return Rxn.DfrCreate<string>(() => Rxn.Create(() =>
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    Process.Start(new ProcessStartInfo()
                    {
                        FileName = "/bin/bash",
                        Arguments = $"-c \"{shellCommand}\" ",
                        CreateNoWindow = true
                    }).WaitForExit();
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Process.Start(new ProcessStartInfo()
                    {
                        FileName = "cmd",
                        Arguments = $"-c \"{shellCommand}\" ",
                        CreateNoWindow = true
                    }).WaitForExit();
                }

                return "done";
            }));
        }

        public void AllowToBeExecuted(string app)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Execute($"chmod 755 {app}").WaitR();
            }
        }
    }
}