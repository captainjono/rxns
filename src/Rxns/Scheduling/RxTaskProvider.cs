using System;
using System.Collections.Generic;
using System.Reactive.Subjects;
using Newtonsoft.Json;

namespace Rxns.Scheduling
{
    public class RxTaskProvider : RxnTaskProvider
    {
        public readonly ReplaySubject<ISchedulableTaskGroup[]> TasksPromise = new ReplaySubject<ISchedulableTaskGroup[]>(1);
        protected ISchedulableTaskGroup[] Groups = {};

        public RxTaskProvider()
        {
            
        }

        public override IObservable<ISchedulableTaskGroup[]> GetTasks()
        {
            return TasksPromise;
        }

        public override void Dispose()
        {
            Groups.DisposeAll();
                
            TasksPromise.OnCompleted();
            TasksPromise.Dispose();

            base.Dispose();
        }
    }
}
