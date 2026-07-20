using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace Rxns.Scheduling
{
    public class DeseriliseTaskProvider : RxTaskProvider
    {
        private string _tasks { get; set; }

        public string SerialisedTasks
        {
            get { return _tasks; }
            set
            {
                if (_tasks != value)
                {
                    try
                    {
                        if (value != null)
                        {
                            OnVerbose("New configuration parsing");

                            var tasks = Parse(value).ToArray();
                            _tasks = value;

                            OnVerbose("New configuration set");
                            TasksPromise.OnNext(tasks);
                        }
                        else
                        {
                            _tasks = null;
                            TasksPromise.OnNext(new ISchedulableTaskGroup[] { });
                        }
                    }
                    catch (Exception e)
                    {
                        OnError(e);
                        TasksPromise.OnNext(new ISchedulableTaskGroup[]{ });
                    }
                }
            }
        }
    }
}
