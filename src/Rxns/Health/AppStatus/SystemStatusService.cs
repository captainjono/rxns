using System;
using System.Collections.Generic;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using Rxns.DDD.Commanding;
using Rxns.Hosting;
using Rxns.Interfaces;
using Rxns.Logging;
using Rxns.Microservices;

namespace Rxns.Health
{
    public class PerformAPing : ServiceCommand
    {

    }

    public class SystemStatusService : ReportStatusService, IServiceCommandHandler<PerformAPing>, IRxnPublisher<IRxn>, IRxnProcessor<PerformAPing>
    {
        public IScheduler DefaultScheduler { get; set; }

        public SystemStatus CurrentStatus { get; set; }
        public TimeSpan SelfRecoveryTimeout { get; set; }
        public TimeSpan StatusHeartBeatInterval { get; set; }

        private readonly IRxnAppInfo _appInfo;
        private readonly IAppContainer _container;
        private IDisposable _selfRecoverySub;
        private readonly List<IDisposable> triggerStatusSubs = new List<IDisposable>();
        private readonly ITenantCredentials _userCfg;
        private readonly bool IsEnabled = false;
        private Action<IRxn> _publish;

        public SystemStatusService(IAppStatusServiceClientCfg cfg, ITenantCredentials userCfg, IRxnAppInfo appInfo, IAppContainer container, IScheduler scheduler = null)
            
        {
            DefaultScheduler = scheduler ?? TaskPoolScheduler.Default;
            _appInfo = appInfo;
            _container = container;
            _userCfg = userCfg;

            CurrentStatus = SystemStatus.Ok;
            
            StatusHeartBeatInterval = cfg.HeartBeatInterval;
            SelfRecoveryTimeout = cfg.SelfRecoveryTimeout;
            IsEnabled = cfg.EnableSupportHeartbeat;
        }


        public IObservable<CommandResult> Handle(PerformAPing command)
        {
            SendSystemStatusEvent();

            return CommandResult.Success().ToObservable();
        }

        public override IObservable<CommandResult> Start(string from = null, string options = null)
        {
            return Rxn.Create(() =>
            {
                if (!IsEnabled) return CommandResult.Success();

                OnInformation("Adding triggers for status heartbeats");
                RemoveAllStatusTriggers();
                TriggerStatusReporter(_container);

                return CommandResult.Success();
            });
        }

        public IObservable<IRxn> Process(PerformAPing @event)
        {
            return Handle(@event);
        }

        public override IObservable<CommandResult> Stop(string from = null)
        {
            return Rxn.Create(() =>
            {

                OnInformation("Removing triggers for status heartbeats");
                RemoveAllStatusTriggers();

                return CommandResult.Success();
            });
        }

        private void RemoveAllStatusTriggers()
        {
            if (triggerStatusSubs.Count > 0)
            {
                triggerStatusSubs.DisposeAll();
                triggerStatusSubs.Clear();
            }
        }

        //could re-write as a puslar which just noops on trigger.. but would still use system resources so..?
        private void TriggerStatusReporter(IAppContainer container)
        {
            //OnVerbose("watching fro errors {0}", reporter.ReporterName);
            container.Errors.Subscribe(this, _ =>
            {
                CurrentStatus = SystemStatus.Error;

                TriggerOnSelfRecovery(() => { CurrentStatus = SystemStatus.Ok; });
            })
            .DisposedBy(triggerStatusSubs);

            //setup the trigger timer
            Observable.Interval(StatusHeartBeatInterval, DefaultScheduler)
                .Subscribe(this, _ => SendSystemStatusEvent())
                .DisposedBy(triggerStatusSubs);
        }

        public void SendSystemStatusEvent()
        {
            try
            {
                OnVerbose("Publishing event: {0}", _appInfo.Name);

                _publish(new SystemStatusEvent()
                {
                    SystemName = _appInfo.Name,
                    Version = _appInfo.Version,
                    Tenant = _userCfg.Tenant,
                    Status = CurrentStatus,
                    KeepUpToDate = _appInfo.KeepUpdated
                });

                OnVerbose("Published");
            }
            catch (Exception e)
            {
                OnWarning(e.ToString());
            }

        }

        private void TriggerOnSelfRecovery(Action onSelfRecovery)
        {
            //stop any existing self recoveries taking place
            if (_selfRecoverySub != null)
                _selfRecoverySub.Dispose();

            _selfRecoverySub = Observable.Timer(SelfRecoveryTimeout, DefaultScheduler).Subscribe(
                _ =>
                {
                    try
                    {
                        onSelfRecovery.Invoke();
                    }
                    catch (Exception e)
                    {
                        OnWarning(e.ToString());
                    }
                });
        }

        public override void Dispose()
        {
            if (_selfRecoverySub != null)
                _selfRecoverySub.Dispose();

            base.Dispose();
        }

        public void ConfigiurePublishFunc(Action<IRxn> publish)
        {
            _publish = publish;
        }

        
    }
}
