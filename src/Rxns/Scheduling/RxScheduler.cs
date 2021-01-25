using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Rxns.Logging;

namespace Rxns.Scheduling
{
    public class RxScheduler : ReportsStatus, ITaskScheduler//, IAmReactive
    {
        public IScheduler DefaultScheduler { get; set; }

        private BehaviorSubject<bool> _isStarted { get; set; }
        public IObservable<bool> IsStarted { get { return _isStarted; } }

        private readonly Dictionary<ISchedulableTaskGroup, IDisposable> _scheduledTasks = new Dictionary<ISchedulableTaskGroup, IDisposable>();
        private readonly List<ISchedulableTaskGroup> _defferedGroups = new List<ISchedulableTaskGroup>();

        public RxScheduler(IScheduler  scheduler= null)
        {
            DefaultScheduler = scheduler ?? new LongScheduler(NewThreadScheduler.Default);
            _isStarted = new BehaviorSubject<bool>(false);
        }

        public void Clear()
        {
            ClearSchedules();
        }

        public void Start()
        {
            _isStarted.SetValue(true);
            ScheduleAllGroups();

            OnVerbose("Scheduler started");
        }

        public void UnSchedule(ISchedulableTaskGroup[] groups)
        {
            groups.WhenAllRunning(false).Wait();

            foreach (var group in groups)
            {
                OnVerbose("Removing schedule for group: {0}", group.Name);

                if (_scheduledTasks.ContainsKey(group))
                {
                    if (_scheduledTasks[group] != null)
                        _scheduledTasks[group].Dispose();
                    _scheduledTasks.Remove(group);
                    return;
                }

                if (_defferedGroups.Contains(group))
                {
                    _defferedGroups.Remove(group);
                    return;
                }

                OnWarning("Cannot find schedule to remove for group: '{0}'", group.Name);
            }
        }

        private void ScheduleAllGroups()
        {
            foreach (var group in _defferedGroups)
                Schedule(group);
        }

        public void Stop()
        {
            OnVerbose("Waiting for running groups to finish");

            _isStarted.SetValue(false);

            ClearSchedules();

            OnVerbose("Stopped");
        }

        public void Pause()
        {
            DeferSchedules();
            _isStarted.SetValue(false);
        }

        private void DeferSchedules()
        {
            foreach (var group in _scheduledTasks)
            {
                OnVerbose("Pausing schedule for group {0}", group.Key.Name);

                group.Value.Dispose();
                _defferedGroups.Add(group.Key);
            }

            _scheduledTasks.Clear();
        }

        private void ClearSchedules()
        {
            _scheduledTasks.Keys.WhenAllRunning(false).Wait();

            foreach (var group in _scheduledTasks)
            {

                if (group.Value != null)
                    group.Value.Dispose();
            }

            _scheduledTasks.Clear();
        }

        public void Resume()
        {
            Start();
        }

        /// <summary>
        /// Schedules a group to be executed immediately by the scheduler
        /// on a new thread.
        /// </summary>
        /// <param name="groups">The groups to schedule</param>
        public IObservable<Unit> Run(ISchedulableTaskGroup group, ExecutionState state = null)
        {
            if (IsStarted.Value())
            {
                group.TaskScheduler = this;
                return Observable.Start(() => group.Execute(state), DefaultScheduler);
            }

            return Observable.Throw<Unit>(new InvalidOperationException("Scheduler must be started to run jobs"));
        }

        /// <summary>
        /// Schedules a series of groups to be executed at a point in the future
        /// By default, groups are executed on new threads to increase isolation of the 
        /// process
        /// </summary>
        /// <param name="groups">The groups to schedule</param>
        public void Schedule(IEnumerable<ISchedulableTaskGroup> groups)
        {
            foreach (var group in groups)
                Schedule(group);
        }

        /// <summary>
        /// Schedules a group to be executed, using the TimeSpan/CronSchedule properties to determin the
        /// when the work will occour. By default, the work is scheduled on a new thread.
        /// </summary>
        /// <param name="group">The group to schedule</param>
        /// <returns>An object that will cancel the scheduled work when disposed</returns>
        public void Schedule(ISchedulableTaskGroup group)
        {
            IDisposable handle = null;

            if (IsStarted.Value())
            {
                //dispose of old timer
                CleanupAfterGroup(group);

                group.TaskScheduler = this;

                //if we have a cronschedule defined
                //if (!String.IsNullOrEmpty(group.CronSchedule))
                //{
                //    handle = ScheduleWithCron(group);

                //    //did we schedule something?
                //    if (handle == null)
                //        return;
                //}
                if (group.TimeSpanSchedule.HasValue)
                {
                    handle = ScheduleWithTimeSpan(group, group.TimeSpanSchedule.Value);

                    //did we schedule something?
                    if (handle == null)
                        return;
                }
                else
                {
                    OnVerbose("Group '{0}' has been added with no schedule", group.Name);
                }

                _scheduledTasks.AddOrReplace(group, handle);
            }
            else
            {
                OnInformation("Deferred scheduling '{0}' until started", group.Name);
                _defferedGroups.Add(group);
            }
        }

        //private IDisposable ScheduleWithCron(ISchedulableTaskGroup group)
        //{
        //    var timeDue = group.CronSchedule.NextOccourance(DefaultScheduler.Now.LocalDateTime);

        //    return ScheduleWithTimeSpan(group, timeDue);
        //}

        /// <summary>
        /// Schedules a group to be executed, using the specified timespan. By default, the work is scheduled
        /// on a new thread
        /// </summary>
        /// <param name="group">The group to execute</param>
        /// <param name="timeDue">The period that will elapse before the group runs</param>
        /// <returns>An object that will cancel the scheduled work when disposed</returns>
        private IDisposable ScheduleWithTimeSpan(ISchedulableTaskGroup group, TimeSpan timeDue)
        {
            var timer = Observable.Timer(timeDue, DefaultScheduler).Subscribe(
                execute =>
                {
                    this.ReportExceptions(() =>
                    {
                        OnInformation("Triggering group '{0}'", group.Name);
                        group.Execute();
                    });
                },
                error =>
                {
                    this.ReportExceptions(() =>
                    {
                        OnError(
                            new Exception(String.Format("An error occoured while running task group '{0}'", group.Name)));
                        //reschedule group anyway
                        Schedule(group);
                    });
                },
                () => //continueWith
                {
                    this.ReportExceptions(() =>
                    {
                        Schedule(group);
                    });
                });

            OnInformation("Scheduling '{0}' to run in '{1}'", group.Name, timeDue);

            return timer;
        }

        private void CleanupAfterGroup(ISchedulableTaskGroup group)
        {
            if (_scheduledTasks.ContainsKey(group))
            {
                if (_scheduledTasks[group] != null)
                    _scheduledTasks[group].Dispose();
            }
        }

        public ISchedulableTaskGroup[] ScheduledGroups()
        {
            if (!IsStarted.Value())
                return _defferedGroups.ToArray();

            return _scheduledTasks.Keys.ToArray();
        }
    }
}
