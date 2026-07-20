using System;
using System.Collections.Generic;
using System.Reactive;
using Newtonsoft.Json;
using Rxns.Logging;

namespace Rxns.Scheduling
{
    public abstract class RxnTaskProvider : ReportsStatus, ITaskProvider
    {
        protected RxnTaskProvider()
        {
        }

        public abstract IObservable<ISchedulableTaskGroup[]> GetTasks();
        
        protected IEnumerable<ISchedulableTaskGroup> Parse(string json)
        {
            //var jsr = new JsonSerializerSettings
            //{
            //    TypeNameHandling = TypeNameHandling.None,
            //    //TypeNameAssemblyFormat = FormatterAssemblyStyle.Simple,
            //    ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            //    Formatting = Formatting.Indented,
            //    DefaultValueHandling = DefaultValueHandling.Ignore,
            //    NullValueHandling = NullValueHandling.Ignore,
            //    PreserveReferencesHandling = PreserveReferencesHandling.None
            //};

            return json.Deserialise<SchedulableTaskGroup[]>();
        }
    }

    public class InlineTasksProvider : RxnTaskProvider
    {
        private readonly ISchedulableTaskGroup[] _tasks;

        public InlineTasksProvider(params ISchedulableTaskGroup[] tasks)
        {
            _tasks = tasks;
        }
        public override IObservable<ISchedulableTaskGroup[]> GetTasks()
        {
            return _tasks.ToObservable();
        }
    }
}
