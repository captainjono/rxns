using System;
using System.Collections.Generic;
using System.IO;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Rxns.Cloud;
using Rxns.Hosting;
using Rxns.Hosting.Updates;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns.Health
{
    public interface IAppStatusServiceClient
    {
        IObservable<Unit> Publish(IEnumerable<IRxn> events);
        IObservable<Unit> PublishError(BasicErrorReport report);

        IObservable<Unit> DeleteError(long id);

        IObservable<IRxnQuestion[]> PublishSystemStatus(SystemStatusEvent status, AppStatusInfo[] meta);

        IObservable<string> PublishLog(Stream zippedLog);
    }

    public interface IAppStatusServiceClientCfg
    {
        bool EnableSupportHeartbeat { get; }
        TimeSpan HeartBeatInterval { get; }
        TimeSpan SelfRecoveryTimeout { get; }
    }

    public class ClientAppStatusErrorChannel : ReportsStatus, IErrorChannel
    {
        private readonly IUserAuthenticationService _authService;
        private readonly IAppStatusServiceClient _appStatus;
        private readonly IDevice _device;
        private readonly List<ErrorReport> _offlineErrors = new List<ErrorReport>();

        public ClientAppStatusErrorChannel(IUserAuthenticationService authService, IAppStatusServiceClient appStatus, IDevice device)
        {
            _authService = authService;
            _appStatus = appStatus;
            _device = device;
            _authService.IsAuthenticated
                        .Where(goodToGo => goodToGo)
                        .Subscribe(this, _ =>
                        {
                            _offlineErrors.ForEach(Send);
                            _offlineErrors.Clear();
                        });
        }

        public void Send(ErrorReport report)
        {
            try
            {
                if (_authService.IsAuthenticated.Value())
                {
                    OnVerbose("Sending error '{0}'", report.Error.Message.ToStringMax(80));

                    _appStatus.PublishError(new BasicErrorReport()
                    {
                        Error = report.Error.Message.Message,
                        StackTrace = "{0}, {1}\r\n{2}".FormatWith(_device.GetOS(), _device.GetVersion(), report.Error.Message.StackTrace ?? (report.Error.Message.InnerException != null ? report.Error.Message.InnerException.StackTrace : null)),
                        Tenant = report.Tenant,
                        System = report.System,
                        Timestamp = report.Timestamp,
                        History = report.History,
                        Reporter = report.Error.Reporter
                    }).WaitR();
                    OnInformation("Sent error '{0}'", report.Error.Message.ToStringMax(80));
                }
                else
                {
                    _offlineErrors.Add(report);
                    OnVerbose("Deffered sending error till connected '{0}'", report.Error.Message.ToStringMax(80));
                }
            }
            catch (Exception e)
            {
                //otherwise we will trip a loop where an error is being sent constantly
                OnWarning("Could not send error because: {0}", e);
            }
        }
    }

    public class ServerAppStatusErrorChannel : ReportsStatus, IErrorChannel
    {
        private readonly IAppStatusServiceClient _appStatus;
        private readonly IAppStatusServiceClientCfg _cfg;

        public ServerAppStatusErrorChannel(IAppStatusServiceClientCfg cfg, IAppStatusServiceClient appStatus)
        {
            _appStatus = appStatus;
            _cfg = cfg;

            

        }

        public void Send(ErrorReport report)
        {
            if (!_cfg.EnableSupportHeartbeat) return;

            _appStatus.PublishError(new BasicErrorReport()
            {
                Error = report.Error.Message.Message,
                StackTrace = "{0}".FormatWith(report.Error.Message.StackTrace ?? (report.Error.Message.InnerException != null ? report.Error.Message.InnerException.StackTrace : null)),
                Tenant = report.Tenant,
                System = report.System,
                Timestamp = report.Timestamp,
                History = report.History,
                Reporter = report.Error.Reporter
            }).WaitR();

            OnVerbose("Sent error '{0}'", report.Error.Message.ToStringMax(80));
        }
    }


    /// <summary>
    /// This service only publishes system status while the user is authenticated, it doesnt automtically trigger authentication like
    /// the publisher-style systsem status publisher
    /// </summary>
    public class AppSystemStatusPublisher : SystemStatusPublisher, IRxnCfg
    {
        private readonly IUserAuthenticationService _authService;
        private readonly Subject<SystemStatusEvent> _statusQueue = new Subject<SystemStatusEvent>();

        public AppSystemStatusPublisher(IAppStatusServiceClient appStatus, IUserAuthenticationService authService, IAppUpdateManager updates) : base(appStatus,  updates)
        {
            _authService = authService;

            //todo: fix ping event
            //authService.IsAuthenticated.Where(goodToGo => goodToGo).Subscribe(_ => eventManager.Publish(new RxnQuestion() { Action = SystemCommand.Ping }));
        }

        public IObservable<IRxn> ConfigureInput(IObservable<IRxn> input)
        {
            return input.OfType<SystemStatusEvent>().CombineLatest(_authService.IsAuthenticated, (s, auth) => new Tuple<SystemStatusEvent, bool>(s, auth))
                .Where(w => w.Item2)
                .Select(s => s.Item1);
        }

        public override IObservable<IRxn> Process(SystemStatusEvent status)
        {
            _statusQueue.OnNext(status);

            return Rxn.Empty<IRxn>();
        }


        public string Reactor { get; }
        public IDeliveryScheme<IRxn> InputDeliveryScheme { get; }
        public bool MonitorHealth { get; }
        public RxnMode Mode { get; }
    }
}
