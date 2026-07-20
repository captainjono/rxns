using System;
using System.Collections.Generic;
using System.Reactive;
using System.Reactive.Linq;
using Rxns.DDD.Commanding;
using Rxns.Interfaces;

namespace Rxns.Logging
{
    /// <summary>
    /// todo: implement serviceCommands  & routeswhich can remotely stop/start this service
    /// </summary>
    public abstract class ReportStatusService : ReportsStatus, IRxnService
    {
        protected readonly List<IDisposable> _onStop;

        protected ReportStatusService()
        {
            _onStop = new List<IDisposable>();
        }
        
        public virtual IObservable<CommandResult> Start(string from = null, string options = null)
        {
            return CommandResult.Failure("Not suppored").ToObservable();
        }

        public virtual IObservable<CommandResult> Stop(string from = null)
        {
            return Run(() =>
            {
                _onStop.DisposeAll();
                _onStop.Clear();
            });
        }

        public virtual IObservable<CommandResult> Setup()
        {
            return CommandResult.Failure("Not suppored").ToObservable();
        }

        protected IObservable<CommandResult> Run(Action work)
        {
            return Rxn.DfrCreate(() =>
            {
                try
                {
                    work();
                }
                catch (Exception e)
                {
                    return CommandResult.Failure(e.Message);
                }
                return CommandResult.Success();
            })
            .Catch<CommandResult, Exception>(e => CommandResult.Failure(e.Message).ToObservable());
        }
    }
}
