using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Subjects;
using Rxns.DDD;
using Rxns.DDD.CQRS;
using Rxns.Health;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns.Hosting
{
    public enum ProcessStatus
    {
        Active,
        Killed,
        Terminated
    }

    public class AppProcessStarted : HealthEvent
    {

    }

    public class AppProcessEnded : HealthEvent
    {

    }

    public class OutOfProcessRxnAppContext : IRxnAppContext
    {
        private readonly string _name;
        public string[] args { get; set; }

        private readonly ReplaySubject<ProcessStatus> _status = new ReplaySubject<ProcessStatus>();
        private readonly Process _process;
        private ProcessStatus _processStatus;
        private CompositeDisposable _resources = new CompositeDisposable();
        public IAppSetup Installer { get; }
        public ICommandService CmdService => Resolver.Resolve<ICommandService>();
        public IAppCommandService AppCmdService => Resolver.Resolve<IAppCommandService>();
        public IRxnManager<IRxn> RxnManager { get; }
        public IResolveTypes Resolver => App.Resolver;
        public IObservable<ProcessStatus> Status => _status;
        public IRxnHostableApp App { get; }


        //i was about to work out who is repsonsible for gettting and setting the dirtory of the apps?
        //the cluster needs this info, but this class does the actual execution..
        //i think the cluster should? we see here that we need to have an actual app before we 
        //can create it. this may not always be the case...?
        public OutOfProcessRxnAppContext(IRxnHostableApp app, IRxnManager<IRxn> rxnManager, string[] args)
        {
            App = app;
            _name = app.AppInfo.Name;
            RxnManager = rxnManager;
            this.args = args;

            if (args.Contains("reactor"))
            {
                _name = $"{_name}[{args.SkipWhile(a => a != "reactor").Skip(1).FirstOrDefault()}]";
            }

            //need to s
            var reactorProcess = new ProcessStartInfo
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                ErrorDialog = false,
                WorkingDirectory = Environment.CurrentDirectory,
            };

            var tokens = app.AppPath.Split(' ');
            if (tokens.Length > 1)
            {
                reactorProcess.FileName = tokens[0];// @"C:\jan\Rxns\Rxns.AppHost\bin\Debug\netcoreapp3.1\Rxn.Create.exe",
                reactorProcess.Arguments = tokens.Skip(1).Concat(args).ToStringEach(" ");
            }
            else
            {
                reactorProcess.FileName = app.AppPath;
                reactorProcess.Arguments = args.ToStringEach(" ");
            }

            _process = new Process
            {
                StartInfo = reactorProcess,
                EnableRaisingEvents = true
            };

            _process.Exited += Process_Exited;
        }


        private void Process_Exited(object sender, EventArgs e)
        {
            LogExitReason();


            //st.Publish(new AppProcessEnded());
            _status.OnNext(ProcessStatus.Terminated);

            //// If Process Status is Active is because it was running and ended unexpectedly, 
            //// If status is Terminated then it was due to Terminate method invoked.
            //if (RestartOnProcessExit && _processStatus != ProcessStatus.Killed)
            //{
            //    $"Restarting process {args.Last()}".LogDebug();
            //    try
            //    {
            //        Start();
            //    }
            //    catch (Exception ex)
            //    {
            //        $"failed to restart {ex}".LogDebug();
            //    }
            //}
        }
        
        /// <summary>
        /// Logs the current action taken.
        /// </summary>
        private void LogExitReason()
        {
            switch (_processStatus)
            {
                case ProcessStatus.Active:
                    "process exited unexpectedly".LogDebug(_name);
                    break;
                case ProcessStatus.Killed:
                    "process was ended".LogDebug(_name);
                    break;
                case ProcessStatus.Terminated:
                    "process restarted".LogDebug(_name);
                    break;
            }
        }
        
        /// <summary>
        /// Starts the remote process which will host an Activator
        /// </summary>
        public IObservable<IRxnAppContext> Start()
        {
            $"Starting process '{_name}'".LogDebug();

            if (!_process.Start())
            {
                throw new Exception(string.Format("Failed to start process from: {0}", _process.StartInfo.FileName));
            }

            _process.KillOnExit();
            $"Process successfully started with process id {_process.Id}".LogDebug();

            //_centralManager.Publish(new AppProcessStarted());
            _status.OnNext(ProcessStatus.Active);

            return this.ToObservable();
        }

        /// <summary>
        /// Terminates this process, then it starts again.
        /// </summary>
        public void Terminate()
        {
            $"Terminating {_name}".LogDebug();

            _processStatus = ProcessStatus.Terminated;

            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill();
                }
            }
            catch (Exception ex)
            {
                $"Failed to terminate {ex}".LogDebug();

                throw;
            }
        }

        /// <summary>
        /// Kills the remote process.
        /// </summary>
        public void Kill()
        {
            $"Killing {_name}".LogDebug();

            _processStatus = ProcessStatus.Killed;

            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill();
                    _process.WaitForExit();
                }
            }
            catch (Exception ex)
            {
                $"Failed to kill {ex}".LogDebug();
                throw;
            }
        }


        public void Dispose()
        {
            _resources.Dispose();
            _resources.Clear();
        }

        public void OnDispose(IDisposable obj)
        {
            _resources.Add(obj);
        }
    }



}
