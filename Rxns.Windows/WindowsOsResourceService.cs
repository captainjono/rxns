using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Text;
using Microsoft.VisualBasic.Devices;
using Rxns.Hosting;
using Rxns.Interfaces;

namespace Rxns.Windows
{
    public class WindowsSystemServices : IOperationSystemServices, IDevice
    {
        public IEnumerable<string> GetServiceUsers(string name)
        {
            var serviceUsernames = new List<string>();

            using (var results = new ManagementObjectSearcher("select displayName, startname from Win32_Service"))
            {

                var users = from ManagementBaseObject service
                            in results.Get()
                            where service["displayName"] != null && service["displayName"].ToString().Contains(name, StringComparison.OrdinalIgnoreCase) //all sql server instances
                                && service["startname"] != null && !service["startname"].ToString().Equals("localsystem", StringComparison.OrdinalIgnoreCase) //already has complete access
                            select service["startname"].ToString();

                if (users.AnyItems())
                    serviceUsernames.AddRange(users);
            }

            return serviceUsernames;
        }

        public int GetSystemCores()
        {
            return new ManagementObjectSearcher("Select NumberOfCores from Win32_Processor").Get().Count;
        }

        public void SetProcessPriority(int priority)
        {
            Process.GetCurrentProcess().PriorityClass = priority == 1 ? ProcessPriorityClass.High : priority == 2 ? ProcessPriorityClass.Normal : ProcessPriorityClass.BelowNormal;
        }

        public void SetProcessorAffinity(IntPtr affinityMask)
        {
            Process.GetCurrentProcess().ProcessorAffinity = affinityMask;
        }

        public IObservable<string> Execute(string shellCommand, IScheduler scheduler = null)
        {
            scheduler = scheduler ?? Scheduler.CurrentThread;

            return Observable.Create<string>(o =>
            {
                return Observable.Start(() =>
                {
                    var processOutput = new StringBuilder();
                    var cmd = new ProcessStartInfo("cmd", String.Format("/c {0}", shellCommand)) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
                    var process = new Process
                    {
                        StartInfo = cmd,
                        EnableRaisingEvents = true,
                    };

                    //helper funcs
                    EventHandler exitedHandler = null;

                    Action<object, EventArgs> exitedFunc = (sender, args) =>
                    {
                        process.WaitForExit();
                        processOutput.Append(process.StandardOutput.ReadToEnd());
                        processOutput.Append(process.StandardError.ReadToEnd());
                        process.Exited -= exitedHandler;

                        if (process.ExitCode != 0)
                        {
                            o.OnError(new InvalidOperationException(processOutput.ToString()));
                        }

                        o.OnNext(processOutput.ToString());
                        o.OnCompleted();
                    };

                    exitedHandler = (s, a) => exitedFunc(s, a);
                    process.Exited += exitedHandler;

                    process.Start();

                }, scheduler)
                .Subscribe();
            });
        }

        public string GetVersion()
        {
            return new ComputerInfo().OSVersion;
        }

        public string GetOS()
        {
            return new ComputerInfo().OSFullName;
        }

        public string GetConnection()
        {
            return "na";
        }
    }
}
