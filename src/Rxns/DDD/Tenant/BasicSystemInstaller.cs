using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using Rxns.Hosting.Updates;
using Rxns.Logging;
using Rxns.Scheduling;

namespace Rxns.DDD.Tenant
{
    public interface ISystemInstaller : IDisposable
    {
        /// <summary>
        /// The time the system will wait for the installation tasks to run before 
        /// throwing an exception and failing the install.
        /// The default value is 2 minutes.
        /// </summary>
        TimeSpan Timeout { get; set; }
        /// <summary>
        /// Runs the installation tasks. Will throw an exception of the tasks fail to run
        /// </summary>
        void Install();
        /// <summary>
        /// Runs the uninstallation tasks
        /// </summary>
        void Uninstall();
    }

    public interface IInstallerConfiguration
    {
        TimeSpan InstallTimeout { get; set; }
        bool ShouldUploadLog { get; set; }
    }

    public class DefaultSystemInstaller : ReportsStatus, ISystemInstaller
    {
        private readonly ITaskProvider _setupTasks;
        private readonly ITaskScheduler _taskRunner;
        private readonly IScheduler _defaultScheduler;
        private readonly IAppUpdateManager _manager;

        public TimeSpan Timeout { get; set; }
        public bool ShouldUploadLog { get; set; }

        public bool ShouldDisposeOfInstallTasks { get; set; }

        public DefaultSystemInstaller(IInstallerConfiguration configuartion, ITaskProvider setupTasks, ITaskScheduler taskRunner, IAppUpdateManager manager, IScheduler defaultScheduler = null)
        {
            ShouldDisposeOfInstallTasks = true;
            Timeout = configuartion.InstallTimeout;
            _defaultScheduler = defaultScheduler ?? Scheduler.Default;
            _taskRunner = taskRunner;
            _manager = manager;
            _setupTasks = setupTasks;
        }

        protected virtual string InstallTask()
        {
            return "install";
        }

        protected virtual string UninstallTask()
        {
            return "uninstall";
        }

        protected virtual void BeforeInstall() { }
        protected virtual void AfterInstall() { }

        protected virtual void Rollback()
        {
            if (ShouldUploadLog)
            {
                OnVerbose("Uploading logfile for analysis");
                this.ReportExceptions(() => _manager.UploadLog(truncate: true));
            }
        }

        public void Install()
        {
            OnVerbose("Looking for setup tasks");
            BeforeInstall();

            var installTasks = _setupTasks.GetTasks()
                                    .FirstAsync()
                                    .Timeout(_defaultScheduler.Now + Timeout, _defaultScheduler) //to ensure the install task doesnt wait forever because of a file-system problem. fail the install immediately!
                                    .Wait()
                                    .DisposedBy(this);

            var installTask = installTasks.FirstOrDefault(t => t.Name.Equals(InstallTask(), StringComparison.InvariantCultureIgnoreCase));

            if (installTask == null)
            {
                OnVerbose("No install tasks found using provider: '{0}'", _taskRunner.GetType().Name);
                return;
            }

            OnVerbose("Beginning installation: {0} tasks to run", installTask.Tasks.Count);
            RunTasks(installTask, installTasks);

            AfterInstall();
            OnInformation("Installation successful");
        }

        /// <summary>
        /// We
        /// </summary>
        /// <param name="installTasks">The task group that installs the desired components</param>
        /// <param name="allTasks">All tasks that the installer task can reference</param>
        private void RunTasks(ISchedulableTaskGroup installTasks, IEnumerable<ISchedulableTaskGroup> allTasks)
        {
            //do the install
            _taskRunner.Start();
            _taskRunner.Schedule(allTasks); //add the tasks incase the install task runs executegroup tasks
            RunInstallTasks(_taskRunner, installTasks).Timeout(_defaultScheduler.Now + Timeout, _defaultScheduler).Wait();

            //fail if an exception was thrown
            if (!installTasks.RanToCompletion())
            {
                this.ReportExceptions(() => Rollback());
                throw new Exception("Installation failed");
            }
        }

        protected virtual IObservable<Unit> RunInstallTasks(ITaskScheduler scheduler, ISchedulableTaskGroup tasks)
        {
            return scheduler.Run(tasks);
        }

        public void Uninstall()
        {
            var uninstallTasks = _setupTasks.GetTasks()
                           .FirstAsync()
                           .Timeout(_defaultScheduler.Now + Timeout, _defaultScheduler) //to ensure the install task doesnt wait forever because of a file-system problem. fail the install immediately!
                           .Wait();

            var uninstallTask = uninstallTasks.FirstOrDefault(t => t.Name.Equals(UninstallTask(), StringComparison.InvariantCultureIgnoreCase));

            if (uninstallTasks == null)
            {
                OnVerbose("No uninstall tasks found using provider: '{0}'", _taskRunner.GetType().Name);
                return;
            }

            RunTasks(uninstallTask, uninstallTasks);
        }
    }
}
