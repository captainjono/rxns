using System;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading.Tasks;
using Rxns.Hosting;

namespace Rxns.Scheduling
{
    /// <summary>
    /// Runs an operating system shell command
    /// </summary>
    public partial class CmdTask : SchedulableTask
    {
        public override string ReporterName
        {
            get { return String.Format("Cmd<{0}>", System.StringExtensions.ToStringMax(Command, 7)); }
        }

        private readonly IOperationSystemServices _osServices;
        
        public IScheduler DefaultScheduler { get; set; }
        
        public CmdTask(IOperationSystemServices osServices, IScheduler defaultScheduler = null)
        {
            DefaultScheduler = defaultScheduler ?? Scheduler.CurrentThread;
            _osServices = osServices;
        }

        protected override void BindToState(ExecutionState state)
        {
            Command = String.IsNullOrWhiteSpace(Command) ? BindToState("{Command}", state).ToString() :
                    BindToState(Command, state).ToString();
            base.BindToState(state);
        }

        public override Task<ExecutionState> ExecuteTaskAsync(ExecutionState state)
        {
            return Execute().Select(_ => state).ToTask();
        }
        
        public IObservable<string> Execute()
        {
            return _osServices.Execute(Command, scheduler: DefaultScheduler);
        }
    }
}
