using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using Rxns.Logging;

namespace Rxns.Scheduling
{
    public class AdaptiveScheduler : ReportsStatus, IRxnScheduler
    {
        public IScheduler DefaultScheduler { get; set; }

        public override string ReporterName
        {
            get { return _reporterName; }
        }

        private readonly ITaskScheduler _taskScheduler;
        private IObservable<ITaskProvider> _configurationService;
        private Dictionary<ITaskProvider, ISchedulableTaskGroup[]> _configurationProviders = new Dictionary<ITaskProvider, ISchedulableTaskGroup[]>();

        private List<IDisposable> _activeGetTaskStreams = new List<IDisposable>(); 

        private IDisposable _configurationStream;
        private string _reporterName;
        private object _statusMeta;

        public AdaptiveScheduler(ITaskScheduler taskScheduler, IObservable<ITaskProvider> configurationService)
        {
            _taskScheduler = taskScheduler;
            _configurationService = configurationService;
            
            _reporterName = String.Format("Scheduler<{0}>", taskScheduler.GetType().Name);
        }
        
        private void MonitorConfigurationForTasks()
        {
            OnInformation("Monitoring configuration for groups");

            if (_configurationStream == null)
            {
                _configurationStream = _configurationService.Do(provider =>
                {
                    //need to dispose of correctly
                    OnVerbose("Using task provider '{0}'", provider.GetType().Name);
                    _configurationProviders.Add(provider, new ISchedulableTaskGroup[] {});

                    ScheduleGroups(provider);
                }).Until(OnError);
            }
        }

        private void ScheduleGroups(ITaskProvider provider)
        {
            OnVerbose("Retrieving groups from {0}", provider.GetType().Name);

            var tasks = provider.GetTasks().Subscribe(groups =>
            {
                OnVerbose("Found '{0}' groups to schedule using provider '{1}'", groups.Length, provider.GetType().Name);

                UnscheduleGroups(_configurationProviders[provider]);

                ScheduleGroups(groups, provider);
            });

            _activeGetTaskStreams.Add(tasks);
        }

        private void UnscheduleGroups(ISchedulableTaskGroup[] schedulableTaskGroup)
        {
            _taskScheduler.UnSchedule(schedulableTaskGroup);
        }

        public void Schedule(ISchedulableTaskGroup group)
        {
            ScheduleGroups((ISchedulableTaskGroup[]) new [] { @group });
        }

        public void Unschedule(ISchedulableTaskGroup group)
        {
            UnscheduleGroups(new[] { group });
        }

        private void ScheduleGroups(ISchedulableTaskGroup[] groups, ITaskProvider provider = null)
        {
            if(groups == null || groups.Length == 0)
                return;

            if (provider != null)
            {
                //add all groups to schedule cache
                _configurationProviders[provider] = groups;
            }

            foreach (var group in groups.Where(g=>g.IsEnabled))
            {
                try
                {
                    OnInformation("Scheduling group '{0}'", group.Name);
                    _taskScheduler.Schedule(group);     
                }
                catch (Exception e)
                {
                    OnError("Cannot schedule group '{0}': {1}", e, group.Name, e);
                }
            }
        }

        /// <summary>
        /// Clears all schedules 
        /// </summary>
        public void Clear()
        {
            _taskScheduler.Clear();    
        }

        public void Start()
        {
            try
            {
                if (_taskScheduler.IsStarted.Value()) return;
                
                _taskScheduler.Start();
                MonitorConfigurationForTasks();

                OnVerbose("Started");
            }
            catch (Exception e)
            {
                OnError(e);
            }
        }

        public ISchedulableTaskGroup[] ScheduledGroups()
        {
            return _taskScheduler.ScheduledGroups();
        }

        public void Pause()
        {
            _taskScheduler.Pause();
            OnVerbose("Paused");
       }

        public void Resume()
        {
            _taskScheduler.Resume();
            OnVerbose("Resumed");
        }

        public void Stop()
        {
            if (_taskScheduler.IsStarted.Value())
            {
                _taskScheduler.Stop();
                
                _configurationProviders.Clear();
                
                if (_configurationStream != null)
                {
                    _configurationStream.Dispose();
                    _configurationStream = null;
                }

                foreach(var stream in _activeGetTaskStreams)
                    stream.Dispose();

                _activeGetTaskStreams.Clear();

                OnVerbose("Stopped");
            }
        }
    }
}
