using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns.Scheduling
{
    /// <summary>
    /// This provider schedules the classes which implement IEventPulseService to run
    /// and announces any results these services return on the eventManager publish channel
    /// </summary>
    public class PulseServiceTaskProvider : ReportsStatus, ITaskProvider
    {
        private readonly IEnumerable<IRxnPulseService> _pulsers;
        private readonly IRxnManager<IRxn> _eventManager;
        private Func<ISchedulableTaskGroup> _taskGroupFactory;

        public PulseServiceTaskProvider(IRxnManager<IRxn> eventManager, IEnumerable<IRxnPulseService> pulsers, Func<ISchedulableTaskGroup> taskGroupFactory)
        {
            _eventManager = eventManager;
            _pulsers = pulsers;
            _taskGroupFactory = taskGroupFactory;
        }
        //not running this
        //need to know why gettasks isnt being called
        //i removed the getscheduler from the container, this could be the problem. should test for it.

        public IObservable<ISchedulableTaskGroup[]> GetTasks()
        {
            return _pulsers.Select(p =>
            {
                var group = _taskGroupFactory();
                group.DisposedBy(this);

                group.IsEnabled = true;
                group.IsReporting = true;
                group.Name = String.Format("Pulser<{0}>", p.GetType().Name);
                group.TimeSpanSchedule = p.Interval;
                group.Tasks = new List<ISchedulableTask>
                {
                    new DyanmicSchedulableTask(state =>
                    {
                        var task = new Subject<Unit>();
                        p.Poll().FinallyR(() => task.OnCompleted()).SelectMany(_eventManager.Publish).Until(OnError);
                        task.WaitR();

                        return state;
                    })
                };
                
                return group;
            })
            .ToArray()
            .ToObservable();
        }
    }
}
